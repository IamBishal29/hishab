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
/// Bulk booking runs the same steps as three phases; only step 2 runs in parallel.
/// </summary>
internal sealed partial class ShipmentService(
    HishabDbContext db,
    ICourierProviderRegistry registry,
    ICourierAccountService accounts,
    ICourierLocationService locations,
    IOrderService orders,
    ICustomerService customers,
    CourierBatchSender sender,
    IRequestContext request,
    TimeProvider clock,
    ILogger<ShipmentService> logger) : IShipmentService
{
    public const string BookingInProgressMessage = "This order is being booked right now. Refresh in a moment.";

    public async Task<OrderDetails> BookAsync(Guid orderId, CourierProvider? courier, CourierDestination? destination, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);
        if (order.Status == OrderStatus.Booked)
        {
            return await orders.GetAsync(orderId, cancellationToken); // already done: a retry gets the same answer
        }

        if (Unbookable(order) is var (code, message))
        {
            throw code == BulkErrors.InvalidTransition ? new ConflictException(message) : Invalid("deliveryAddress", message);
        }

        var target = await ResolveCourierAsync(courier, cancellationToken);
        await CheckDestinationAsync(target, destination, cancellationToken);
        var booking = await PrepareAsync(order, target, destination, cancellationToken);

        // CourierUnavailableException passes through: the intent stays Requested and a retry is safe.
        var result = await target.Adapter.BookAsync(target.Keys, booking.Parcel, cancellationToken);

        var refusal = await SaveOutcomeAsync(booking, target.Provider, result, cancellationToken);
        return refusal is null
            ? await orders.GetAsync(orderId, cancellationToken)
            : throw Invalid("courier", refusal);
    }

    public async Task<BulkActionResult> BookManyAsync(IReadOnlyList<Guid> orderIds, CourierProvider? courier, CancellationToken cancellationToken)
    {
        // No usable courier fails the whole request: nothing could be booked anyway.
        var target = await ResolveCourierAsync(courier, cancellationToken);
        var ids = orderIds.Distinct().ToList();
        var results = new Dictionary<Guid, BulkItemResult>();
        var pending = new List<PendingBooking>();

        // Phase 1, one order at a time (a DbContext is not thread-safe): check each order, commit its intent.
        var found = await db.Orders.AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        foreach (var id in ids)
        {
            if (!found.TryGetValue(id, out var order))
            {
                results[id] = Failed(id, null, BulkErrors.NotFound, "Order not found.");
            }
            else if (order.Status == OrderStatus.Booked)
            {
                results[id] = Succeeded(id); // same answer as a retry of a single booking
            }
            else if (Unbookable(order) is var (code, message))
            {
                results[id] = Failed(id, order.Status, code, message);
            }
            else
            {
                try
                {
                    pending.Add(await PrepareAsync(order, target, destination: null, cancellationToken));
                }
                catch (ConflictException e)
                {
                    results[id] = Failed(id, order.Status, BulkErrors.ConcurrentChange, e.Message);
                }
                finally
                {
                    db.ChangeTracker.Clear(); // a failed intent must not be saved again with the next order
                }
            }
        }

        // Phase 2, bounded parallel: only the courier calls, no database work.
        var answers = await sender.SendAsync(target.Adapter, target.Keys, [.. pending.Select(p => p.Parcel)], cancellationToken);

        // Phase 3, one order at a time. CancellationToken.None on purpose: these parcels may exist at
        // the courier now, so their outcome is saved even if the caller has gone away.
        foreach (var booking in pending)
        {
            results[booking.OrderId] = await SaveBulkOutcomeAsync(booking, target.Provider, answers[booking.Parcel.MerchantReference]);
        }

        var ordered = ids.Select(id => results[id]).ToList();
        var succeeded = ordered.Count(r => r.Succeeded);
        LogBulkBooked(logger, target.Provider, succeeded, ordered.Count - succeeded);
        return new BulkActionResult(succeeded, ordered.Count - succeeded, ordered);
    }

    private async Task<BulkItemResult> SaveBulkOutcomeAsync(PendingBooking booking, CourierProvider provider, BookingResult? answer)
    {
        if (answer is null)
        {
            // Stays Requested: booking it again resends the same reference, never a second parcel.
            return Failed(booking.OrderId, OrderStatus.Confirmed, BulkErrors.CourierUnavailable, new CourierUnavailableException(provider).Message);
        }

        try
        {
            var refusal = await SaveOutcomeAsync(booking, provider, answer, CancellationToken.None);
            return refusal is null
                ? Succeeded(booking.OrderId)
                : Failed(booking.OrderId, OrderStatus.Confirmed, BulkErrors.CourierRejected, refusal);
        }
        catch (ConflictException e)
        {
            return Failed(booking.OrderId, null, BulkErrors.ConcurrentChange, e.Message);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Why this order cannot be booked (bulk error code and message), or null when it can.</summary>
    private static (string Code, string Message)? Unbookable(Order order) =>
        order.Status != OrderStatus.Confirmed
            ? (BulkErrors.InvalidTransition, $"Only confirmed orders can be booked. This order is {order.Status}.")
            : string.IsNullOrWhiteSpace(order.DeliveryAddress)
                ? (BulkErrors.Invalid, "Add a delivery address before booking.")
                : null;

    public async Task<CourierQuote> QuoteAsync(Guid orderId, CourierProvider? courier, CourierDestination? destination, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);
        var target = await ResolveCourierAsync(courier, cancellationToken);
        await CheckDestinationAsync(target, destination, cancellationToken);
        var customer = await customers.GetAsync(order.CustomerId, cancellationToken);

        // Nothing is saved: a quote is a question, not an intent.
        return await target.Adapter.QuoteAsync(target.Keys, new ParcelRequest(
            Shipment.ReferenceFor(order.Id),
            customer.Name ?? "Customer",
            customer.Phone,
            order.DeliveryAddress ?? string.Empty,
            order.IsInsideDhaka,
            order.CodAmount,
            order.Lines.Sum(l => l.Quantity),
            order.Note,
            destination), cancellationToken);
    }

    /// <summary>A destination only for couriers that use one, and only IDs from the courier's own lists.</summary>
    private async Task CheckDestinationAsync(CourierTarget target, CourierDestination? destination, CancellationToken cancellationToken)
    {
        if (destination is null)
        {
            return;
        }

        if (!target.Adapter.SupportsLocations)
        {
            throw Invalid("destination", $"{target.Provider} delivers by address; it does not use delivery locations.");
        }

        await locations.EnsureValidAsync(target.Provider, destination, cancellationToken);
    }

    private async Task<CourierTarget> ResolveCourierAsync(CourierProvider? courier, CancellationToken cancellationToken)
    {
        var provider = courier
            ?? (await db.Shops.AsNoTracking().SingleAsync(cancellationToken)).DefaultCourier
            ?? throw Invalid("courier", "Choose a courier, or set a default courier in shop settings.");
        var adapter = registry.Find(provider) ?? throw Invalid("courier", $"{provider} is not available yet.");
        return new CourierTarget(provider, adapter, await KeysFor(provider, cancellationToken));
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

    /// <summary>Step 1 (the intent) plus the parcel the courier will receive.</summary>
    private async Task<PendingBooking> PrepareAsync(Order order, CourierTarget target, CourierDestination? destination, CancellationToken cancellationToken)
    {
        var shipment = await RecordIntentAsync(order.Id, target.Provider, destination, cancellationToken);
        var customer = await customers.GetAsync(order.CustomerId, cancellationToken);
        var parcel = new ParcelRequest(
            shipment.MerchantReference,
            customer.Name ?? "Customer",
            customer.Phone,
            order.DeliveryAddress!,
            order.IsInsideDhaka,
            order.CodAmount,
            order.Lines.Sum(l => l.Quantity),
            order.Note,
            // From the saved intent, not the request: a retry sends exactly what the first attempt sent.
            target.Adapter.SupportsLocations ? shipment.Destination : null);
        return new PendingBooking(order.Id, shipment.Id, parcel);
    }

    /// <summary>Committed before the courier is called, so a parcel never exists without a trace here.</summary>
    private async Task<Shipment> RecordIntentAsync(Guid orderId, CourierProvider provider, CourierDestination? destination, CancellationToken cancellationToken)
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
            shipment = Shipment.Request(orderId, provider, now, destination);
            db.Shipments.Add(shipment);
        }
        else
        {
            shipment.RequestAgain(now, destination); // Rejected -> Requested; an unfinished Requested stays as it is
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

    /// <summary>Step 3: saves the courier's answer. Returns the refusal shown to the user, or null when booked.</summary>
    private Task<string?> SaveOutcomeAsync(PendingBooking booking, CourierProvider provider, BookingResult result, CancellationToken cancellationToken) =>
        result.Succeeded
            ? CompleteAsync(booking.OrderId, booking.ShipmentId, result, cancellationToken)
            : RejectAsync(booking.ShipmentId, provider, result, cancellationToken);

    /// <summary>The shipment and the order change together, or not at all.</summary>
    private async Task<string?> CompleteAsync(Guid orderId, Guid shipmentId, BookingResult result, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var shipment = await db.Shipments.SingleAsync(s => s.Id == shipmentId, cancellationToken);
        if (shipment.State == ShipmentState.Booked)
        {
            return null; // a parallel request with the same reference finished first
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
                return null; // the parallel request saved the same parcel
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
        return null;
    }

    private async Task<string?> RejectAsync(Guid shipmentId, CourierProvider provider, BookingResult result, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var shipment = await db.Shipments.SingleAsync(s => s.Id == shipmentId, cancellationToken);
        var reason = result.Error ?? "The courier refused this parcel.";
        shipment.MarkRejected(reason);
        await db.SaveChangesAsync(cancellationToken);
        LogRejected(logger, shipment.OrderId, provider); // the courier's text may echo personal data: not logged
        return $"{provider} refused this parcel: {reason}";
    }

    private static BulkItemResult Succeeded(Guid id) => new(id, true, OrderStatus.Booked, null, null);

    private static BulkItemResult Failed(Guid id, OrderStatus? status, string code, string message) => new(id, false, status, code, message);

    private static DomainValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private sealed record CourierTarget(CourierProvider Provider, ICourierProvider Adapter, CourierCredentials Keys);

    private sealed record PendingBooking(Guid OrderId, Guid ShipmentId, ParcelRequest Parcel);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} booked with {Provider} (shipment {ShipmentId})")]
    private static partial void LogBooked(ILogger logger, Guid orderId, CourierProvider provider, Guid shipmentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId}: {Provider} refused the parcel")]
    private static partial void LogRejected(ILogger logger, Guid orderId, CourierProvider provider);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bulk booking with {Provider}: {Succeeded} succeeded, {Failed} failed")]
    private static partial void LogBulkBooked(ILogger logger, CourierProvider provider, int succeeded, int failed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderId} changed while booking: parcel {ConsignmentId} at {Provider} needs review")]
    private static partial void LogOrphanedParcel(ILogger logger, Guid orderId, CourierProvider provider, string consignmentId);
}
