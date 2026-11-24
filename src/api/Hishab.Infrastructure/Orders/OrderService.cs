using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hishab.Application.Common.Auditing;
using Hishab.Application.Common.Tenancy;
using Hishab.Application.Customers;
using Hishab.Application.Orders;
using Hishab.Application.Shops;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Orders;
using Hishab.Domain.Products;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hishab.Infrastructure.Orders;

internal sealed partial class OrderService(
    HishabDbContext db,
    ICustomerService customers,
    ICustomerOrderHistory history,
    ICustomerRiskAssessor risk,
    IPlanLimitService limits,
    ITenantContext tenant,
    IRequestContext request,
    TimeProvider clock,
    ILogger<OrderService> logger) : IOrderService
{
    public const string CreateOperation = "CreateOrder";
    public const string KeyReusedMessage = "This idempotency key was already used for a different order. Use a new key for a new order.";

    private const string IdempotencyPrimaryKey = "pk_idempotency_keys";

    private static readonly string[] _unknownVariant = ["This product is not in your catalogue (it may have been deleted)."];

    public async Task<CreateOrderResult> CreateAsync(CreateOrderCommand command, string idempotencyKey, CancellationToken cancellationToken)
    {
        var phone = Phone.Parse(command.Phone);
        var requestHash = Fingerprint(command, phone);

        // A retry after the first attempt committed: answer with the first order, do nothing else.
        if (await FindReplayAsync(idempotencyKey, requestHash, cancellationToken) is { } replay)
        {
            return replay;
        }

        var variants = await LoadVariantsAsync(command.Lines, cancellationToken); // fails fast, before any lock

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var (customer, _) = await customers.FindOrCreateAsync(phone.Value, command.CustomerName, command.DeliveryAddress, cancellationToken);
            await limits.ReserveOrderAsync(cancellationToken); // rolled back with the order if anything below fails
            var number = await NextOrderNumberAsync(cancellationToken);
            var shop = await db.Shops.AsNoTracking().SingleAsync(cancellationToken);

            var deliveryCharge = command.DeliveryCharge is { } overridden
                ? Money.Bdt(overridden, "deliveryCharge")
                : command.IsInsideDhaka ? shop.DeliveryChargeInsideDhaka : shop.DeliveryChargeOutsideDhaka;

            var order = Order.Create(
                number,
                customer.Id,
                command.DeliveryAddress ?? customer.Address,
                command.IsInsideDhaka,
                command.Source,
                [.. command.Lines.Select(line => Snapshot(variants[line.VariantId], line.Quantity))],
                deliveryCharge,
                Money.Bdt(command.AdvancePaid, "advancePaid"),
                command.Note,
                request.UserId,
                clock.GetUtcNow());

            db.Orders.Add(order);
            db.IdempotencyRecords.Add(new IdempotencyRecord(idempotencyKey, CreateOperation, requestHash, order.Id, clock.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            LogCreated(logger, order.Id, order.Number);
            return new CreateOrderResult(await DetailsAsync(order, cancellationToken), Replayed: false);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: IdempotencyPrimaryKey })
        {
            // A parallel request with the same key committed first (our insert waited on the key's
            // index, then failed). Undo everything ours did (quota, number, customer) and answer
            // with the winner's order.
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return await FindReplayAsync(idempotencyKey, requestHash, cancellationToken)
                ?? throw new InvalidOperationException("Idempotency record vanished after a key conflict.");
        }
    }

    public async Task<OrderDetails> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException("Order", id);
        return await DetailsAsync(order, cancellationToken);
    }

    private async Task<CreateOrderResult?> FindReplayAsync(string key, string requestHash, CancellationToken cancellationToken)
    {
        var record = await db.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Key == key && r.Operation == CreateOperation, cancellationToken);
        if (record is null)
        {
            return null;
        }

        if (record.RequestHash != requestHash)
        {
            throw new ConflictException(KeyReusedMessage);
        }

        LogReplayed(logger, record.ResourceId);
        return new CreateOrderResult(await GetAsync(record.ResourceId, cancellationToken), Replayed: true);
    }

    /// <summary>Active variants only (the soft-delete filter hides deleted ones), with their product name.</summary>
    private async Task<Dictionary<Guid, (ProductVariant Variant, string ProductName)>> LoadVariantsAsync(
        IReadOnlyList<OrderLineInput> lines, CancellationToken cancellationToken)
    {
        var ids = lines.Select(l => l.VariantId).Distinct().ToList();
        var found = await db.Products.AsNoTracking()
            .SelectMany(p => p.Variants, (p, v) => new { Variant = v, ProductName = p.Name })
            .Where(x => ids.Contains(x.Variant.Id))
            .ToDictionaryAsync(x => x.Variant.Id, x => (x.Variant, x.ProductName), cancellationToken);

        var errors = lines
            .Select((line, i) => (line, i))
            .Where(x => !found.ContainsKey(x.line.VariantId))
            .ToDictionary(x => $"lines[{x.i}].variantId", _ => _unknownVariant);

        return errors.Count > 0 ? throw new DomainValidationException(errors) : found;
    }

    /// <summary>Copies the catalogue as it is right now into the order line.</summary>
    private static OrderLineDetails Snapshot((ProductVariant Variant, string ProductName) item, int quantity)
    {
        var (variant, productName) = item;
        string[] options = [.. new[] { variant.Size, variant.Colour }.OfType<string>()];
        return new OrderLineDetails(
            variant.Id,
            productName,
            options.Length > 0 ? string.Join(" / ", options) : null,
            variant.Sku,
            variant.Price,
            variant.CostPrice,
            quantity);
    }

    /// <summary>
    /// Takes the next number by incrementing the shop's counter in this transaction. The row lock
    /// orders concurrent orders of one shop; a rolled-back order gives its number back.
    /// </summary>
    private async Task<int> NextOrderNumberAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Creating an order needs a tenant.");
        var numbers = await db.Database.SqlQuery<int>($"""
            UPDATE shops SET last_order_number = last_order_number + 1
            WHERE id = {tenantId}
            RETURNING last_order_number AS "Value"
            """).ToListAsync(cancellationToken);
        return numbers.Single();
    }

    private async Task<OrderDetails> DetailsAsync(Order order, CancellationToken cancellationToken)
    {
        var customer = await customers.GetAsync(order.CustomerId, cancellationToken);
        var record = await history.ForAsync(order.CustomerId, order.Id, cancellationToken);
        var summary = new CustomerSummary(
            customer.Id,
            customer.Phone,
            customer.Name,
            customer.IsBlocked,
            customer.BlockedReason,
            record.PreviousOrders,
            record.Delivered,
            record.Returned,
            record.Cancelled,
            risk.Assess(record, customer.IsBlocked));

        IReadOnlyList<string> warnings = customer.IsBlocked ? [OrderWarnings.CustomerBlocked] : [];
        var shipment = await db.Shipments.AsNoTracking()
            .Where(s => s.OrderId == order.Id)
            .OrderByDescending(s => s.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return new OrderDetails(ToDto(order), summary, warnings, shipment is null ? null : ToDto(shipment));
    }

    internal static ShipmentDto ToDto(Shipment s) =>
        new(s.Id, s.Provider, s.State, s.ConsignmentId, s.TrackingCode, s.DeliveryFee?.Amount, s.Error, s.RequestedAt, s.BookedAt, s.Destination);

    private OrderDto ToDto(Order o)
    {
        var canSeeCost = request.Has(Permission.SeeProfit);
        return new OrderDto(
            o.Id,
            o.Number,
            o.Status,
            o.Source,
            o.DeliveryAddress,
            o.IsInsideDhaka,
            [.. o.Lines.Select(l => new OrderLineDto(
                l.ProductVariantId, l.ProductName, l.VariantLabel, l.Sku, l.UnitPrice.Amount,
                canSeeCost ? l.UnitCost?.Amount : null, l.Quantity, l.LineTotal.Amount))],
            o.Subtotal.Amount,
            o.DeliveryCharge.Amount,
            o.Total.Amount,
            o.AdvancePaid.Amount,
            o.CodAmount.Amount,
            o.Note,
            o.CreatedAt,
            [.. o.History.Select(h => new OrderStatusChangeDto(h.From, h.To, h.ChangedBy, h.ChangedAt, h.Note))]);
    }

    /// <summary>SHA-256 of the request with the phone normalised, so "017..." and "+88017..." count as the same request.</summary>
    private static string Fingerprint(CreateOrderCommand command, Phone phone) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command with { Phone = phone.Value }))));

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} created as #{OrderNumber}")]
    private static partial void LogCreated(ILogger logger, Guid orderId, int orderNumber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotent replay of order {OrderId}")]
    private static partial void LogReplayed(ILogger logger, Guid orderId);
}
