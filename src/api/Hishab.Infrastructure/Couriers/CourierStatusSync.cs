using System.Linq.Expressions;
using Hishab.Application.Couriers;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Orders;
using Hishab.Infrastructure.Jobs;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hishab.Infrastructure.Couriers;

/// <summary>What one shop's sync did.</summary>
public sealed record StatusSyncSummary(int Checked, int Moved, int Failed, bool CourierUnavailable)
{
    public static readonly StatusSyncSummary Empty = new(0, 0, 0, false);
}

/// <summary>
/// Asks one courier about the current shop's parcels on the road and moves their orders along
/// (Booked → InTransit → Delivered / Returned). Runs for the tenant already set on this scope, so
/// the usual tenant filter keeps it inside one shop. Safe to run twice: a status already applied
/// changes nothing, and a concurrent change to an order skips it until the next run.
/// </summary>
internal sealed partial class CourierStatusSync(
    HishabDbContext db,
    ICourierProviderRegistry registry,
    ICourierAccountService accounts,
    IOptions<JobsOptions> options,
    TimeProvider clock,
    ILogger<CourierStatusSync> logger)
{
    /// <summary>
    /// Booked and not finished at the courier. Written as plain comparisons so PostgreSQL can match it
    /// to the partial index <c>ix_shipments_tracking</c> (same predicate).
    /// </summary>
    internal static readonly Expression<Func<Shipment, bool>> IsTracking = s =>
        s.State == ShipmentState.Booked
        && (s.ParcelStatus == null
            || (s.ParcelStatus != ParcelStatus.Delivered && s.ParcelStatus != ParcelStatus.Returned && s.ParcelStatus != ParcelStatus.Cancelled));

    public async Task<StatusSyncSummary> SyncAsync(CourierProvider provider, CancellationToken cancellationToken)
    {
        var adapter = registry.Find(provider);
        var keys = adapter is null ? null : await KeysFor(provider, cancellationToken);
        if (adapter is null || keys is null)
        {
            LogNotConnected(logger, provider); // disconnected since the dispatcher looked: nothing to ask
            return StatusSyncSummary.Empty;
        }

        var batch = await db.Shipments.AsNoTracking()
            .Where(IsTracking)
            .Where(s => s.Provider == provider)
            .OrderBy(s => s.StatusCheckedAt != null) // never checked first, then the oldest check
            .ThenBy(s => s.StatusCheckedAt)
            .Take(options.Value.StatusSyncBatchSize)
            .Select(s => new { s.Id, s.ConsignmentId })
            .ToListAsync(cancellationToken);

        int moved = 0, failed = 0, done = 0;
        foreach (var parcel in batch)
        {
            ParcelStatusResult status;
            try
            {
                status = await adapter.GetStatusAsync(keys, parcel.ConsignmentId!, cancellationToken);
            }
            catch (CourierUnavailableException)
            {
                // The courier is down: asking about the rest now only adds load. The next run continues.
                LogCourierUnavailable(logger, provider, done, batch.Count);
                return new StatusSyncSummary(done, moved, failed, true);
            }
            catch (CourierAuthExpiredException)
            {
                // Every call would fail the same way until the seller reconnects: stop here.
                LogAuthExpired(logger, provider);
                return new StatusSyncSummary(done, moved, failed, true);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogStatusFailed(logger, provider, parcel.Id, e.GetType().Name); // one bad answer must not stop the shop's sync
                failed++;
                done++;
                continue;
            }

            if (await ApplyAsync(parcel.Id, provider, status, cancellationToken))
            {
                moved++;
            }

            done++;
        }

        return new StatusSyncSummary(done, moved, failed, false);
    }

    /// <summary>Saves the status and the order moves in one SaveChanges. True when the order moved.</summary>
    private async Task<bool> ApplyAsync(Guid shipmentId, CourierProvider provider, ParcelStatusResult status, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var shipment = await db.Shipments.SingleAsync(s => s.Id == shipmentId, cancellationToken);
        if (!shipment.IsTracking)
        {
            return false; // a parallel run finished it meanwhile
        }

        var order = await db.Orders.SingleAsync(o => o.Id == shipment.OrderId, cancellationToken);
        var decision = ParcelStatusRules.Decide(order.Status, status.Status);
        var now = clock.GetUtcNow();
        shipment.RecordStatus(status.Status, status.CourierStatus, status.UpdatedAt, now);
        foreach (var move in decision.Moves)
        {
            order.TransitionTo(move, by: null, now, $"Auto-synced from {provider}: {status.Status}"); // null = the system
        }

        if (status.Status == ParcelStatus.Unknown)
        {
            LogUnknownStatus(logger, provider, shipment.Id, status.CourierStatus ?? "(none)");
        }
        else if (decision.NeedsReview)
        {
            LogNeedsReview(logger, order.Id, provider, status.Status, order.Status);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogConcurrentChange(logger, order.Id); // staff or another run changed it: try again next run
            return false;
        }

        if (decision.Moves.Count > 0)
        {
            LogMoved(logger, order.Id, provider, order.Status);
        }

        return decision.Moves.Count > 0;
    }

    private async Task<CourierCredentials?> KeysFor(CourierProvider provider, CancellationToken cancellationToken)
    {
        try
        {
            return await accounts.GetCredentialsAsync(provider, cancellationToken);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} moved to {Status} by {Provider} status sync")]
    private static partial void LogMoved(ILogger logger, Guid orderId, CourierProvider provider, OrderStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Provider} is not connected any more: status sync skipped")]
    private static partial void LogNotConnected(ILogger logger, CourierProvider provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Provider} did not answer: status sync stopped after {Done} of {Total} parcels")]
    private static partial void LogCourierUnavailable(ILogger logger, CourierProvider provider, int done, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Provider} refused the saved connection: status sync stopped until the shop reconnects")]
    private static partial void LogAuthExpired(ILogger logger, CourierProvider provider);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Provider} status for shipment {ShipmentId} failed with {ExceptionType}")]
    private static partial void LogStatusFailed(ILogger logger, CourierProvider provider, Guid shipmentId, string exceptionType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Provider} reported unknown status {CourierStatus} for shipment {ShipmentId}")]
    private static partial void LogUnknownStatus(ILogger logger, CourierProvider provider, Guid shipmentId, string courierStatus);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Order {OrderId} needs review: {Provider} says {ParcelStatus} but the order is {OrderStatus}")]
    private static partial void LogNeedsReview(ILogger logger, Guid orderId, CourierProvider provider, ParcelStatus parcelStatus, OrderStatus orderStatus);

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} changed during status sync: retried next run")]
    private static partial void LogConcurrentChange(ILogger logger, Guid orderId);
}
