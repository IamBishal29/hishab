using Hishab.Application.Common.Auditing;
using Hishab.Application.Couriers;
using Hishab.Application.Customers;
using Hishab.Application.Orders;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Orders;
using Hishab.Infrastructure.Orders;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hishab.Infrastructure.Couriers;

/// <summary>
/// Books an order in three steps, each safe to repeat:
/// 1. save the intent (Shipment = Requested) and commit, before any network call;
/// 2. ask the courier, always with the same reference (the order ID);
/// 3. save the outcome: Shipment = Booked and Order = Booked in one SaveChanges.
/// If step 3 never happens (crash, lost response), the Requested row remains; a retry repeats
/// step 2 with the same reference and the courier returns the same parcel, never a second one.
/// </summary>
internal sealed partial class ShipmentService(
    HishabDbContext db,
    ICourierProviderRegistry registry,
    ICourierAccountService accounts,
    IOrderService orders,
    ICustomerService customers,
    IRequestContext request,
    TimeProvider clock,
    ILogger<ShipmentService> logger) : IShipmentService
{
    public const string BookingInProgressMessage = "This order is being booked right now. Refresh in a moment.";

    public async Task<OrderDetails> BookAsync(Guid orderId, CourierProvider? courier, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);
        if (order.Status == OrderStatus.Booked)
        {
            return await orders.GetAsync(orderId, cancellationToken); // already done: a retry gets the same answer
        }

        if (order.Status != OrderStatus.Confirmed)
        {
            throw new ConflictException($"Only confirmed orders can be booked. This order is {order.Status}.");
        }

        if (string.IsNullOrWhiteSpace(order.DeliveryAddress))
        {
            throw Invalid("deliveryAddress", "Add a delivery address before booking.");
        }

        var provider = courier
            ?? (await db.Shops.AsNoTracking().SingleAsync(cancellationToken)).DefaultCourier
            ?? throw Invalid("courier", "Choose a courier, or set a default courier in shop settings.");
        var adapter = registry.Find(provider) ?? throw Invalid("courier", $"{provider} is not available yet.");
        var keys = await KeysFor(provider, cancellationToken);

        var shipment = await RecordIntentAsync(order.Id, provider, cancellationToken);

        var customer = await customers.GetAsync(order.CustomerId, cancellationToken);
        var parcel = new ParcelRequest(
            shipment.MerchantReference,
            customer.Name ?? "Customer",
            customer.Phone,
            order.DeliveryAddress,
            order.IsInsideDhaka,
            order.CodAmount,
            order.Lines.Sum(l => l.Quantity),
            order.Note);

        // CourierUnavailableException passes through: the intent stays Requested and a retry is safe.
        var result = await adapter.BookAsync(keys, parcel, cancellationToken);

        return result.Succeeded
            ? await CompleteAsync(order.Id, shipment.Id, result, cancellationToken)
            : await RejectAsync(shipment.Id, provider, result, cancellationToken);
    }

    private async Task<CourierCredentials> KeysFor(CourierProvider provider, CancellationToken cancellationToken)
    {
        try
        {
            return await accounts.GetCredentialsAsync(provider, cancellationToken);
        }
        catch (NotFoundException)
        {
            throw Invalid("courier", $"Connect {provider} in courier settings before booking.");
        }
    }

    /// <summary>Step 1: committed before the courier is called, so a parcel never exists without a trace here.</summary>
    private async Task<Shipment> RecordIntentAsync(Guid orderId, CourierProvider provider, CancellationToken cancellationToken)
    {
        var live = await db.Shipments
            .Where(s => s.OrderId == orderId && (s.State == ShipmentState.Requested || s.State == ShipmentState.Booked))
            .Select(s => s.Provider)
            .ToListAsync(cancellationToken);
        if (live.Any(p => p != provider))
        {
            // The other courier may already hold a parcel for this order: booking a second one could
            // send two riders. Finish (retry) that booking first.
            throw new ConflictException($"A booking with {live.First(p => p != provider)} is not finished for this order. Retry with that courier.");
        }

        var now = clock.GetUtcNow();
        var shipment = await db.Shipments.SingleOrDefaultAsync(s => s.OrderId == orderId && s.Provider == provider, cancellationToken);
        if (shipment is null)
        {
            shipment = Shipment.Request(orderId, provider, now);
            db.Shipments.Add(shipment);
        }
        else
        {
            shipment.RequestAgain(now); // Rejected -> Requested; an unfinished Requested stays as it is
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (e is DbUpdateConcurrencyException
            || e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException(BookingInProgressMessage); // a parallel request created the intent first
        }

        return shipment;
    }

    /// <summary>Step 3: the shipment and the order change together, or not at all.</summary>
    private async Task<OrderDetails> CompleteAsync(Guid orderId, Guid shipmentId, BookingResult result, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var shipment = await db.Shipments.SingleAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment.State == ShipmentState.Booked)
        {
            return await orders.GetAsync(orderId, cancellationToken); // a parallel request with the same reference finished first
        }

        var order = await db.Orders.SingleAsync(o => o.Id == orderId, cancellationToken);
        var now = clock.GetUtcNow();
        shipment.MarkBooked(result.ConsignmentId!, result.TrackingCode, result.DeliveryFee, now);
        try
        {
            order.TransitionTo(OrderStatus.Booked, request.UserId, now, $"Booked with {shipment.Provider}: {result.ConsignmentId}");
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var current = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId, cancellationToken);
            if (current.Status == OrderStatus.Booked)
            {
                return await orders.GetAsync(orderId, cancellationToken); // the parallel request saved the same parcel
            }

            LogOrphanedParcel(logger, orderId, shipment.Provider, result.ConsignmentId!);
            throw new ConflictException(OrderService.ConcurrentChangeMessage);
        }
        catch (ConflictException)
        {
            // The order moved on (e.g. cancelled) while the courier was booking. The parcel exists at
            // the courier; the shipment stays Requested so reconciliation (#22) can flag it.
            LogOrphanedParcel(logger, orderId, shipment.Provider, result.ConsignmentId!);
            throw;
        }

        LogBooked(logger, orderId, shipment.Provider, shipment.Id);
        return await orders.GetAsync(orderId, cancellationToken);
    }

    private async Task<OrderDetails> RejectAsync(Guid shipmentId, CourierProvider provider, BookingResult result, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var shipment = await db.Shipments.SingleAsync(s => s.Id == shipmentId, cancellationToken);
        var reason = result.Error ?? "The courier refused this parcel.";
        shipment.MarkRejected(reason);
        await db.SaveChangesAsync(cancellationToken);
        LogRejected(logger, shipment.OrderId, provider); // the courier's text may echo personal data: not logged
        throw Invalid("courier", $"{provider} refused this parcel: {reason}");
    }

    private static DomainValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} booked with {Provider} (shipment {ShipmentId})")]
    private static partial void LogBooked(ILogger logger, Guid orderId, CourierProvider provider, Guid shipmentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId}: {Provider} refused the parcel")]
    private static partial void LogRejected(ILogger logger, Guid orderId, CourierProvider provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderId} changed while booking: parcel {ConsignmentId} at {Provider} needs review")]
    private static partial void LogOrphanedParcel(ILogger logger, Guid orderId, CourierProvider provider, string consignmentId);
}
