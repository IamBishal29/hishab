using Hishab.Application.Orders;
using Hishab.Domain.Common;
using Hishab.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hishab.Infrastructure.Orders;

internal sealed partial class OrderService
{
    public const string ConcurrentChangeMessage = "Someone else changed this order at the same time. Reload it and try again.";

    public async Task<OrderDetails> TransitionAsync(Guid id, OrderStatus target, string? note, CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == id, cancellationToken)
            ?? throw new NotFoundException("Order", id);

        order.TransitionTo(target, request.UserId, clock.GetUtcNow(), note); // 409 if the lifecycle forbids it
        await SaveOrConflictAsync(cancellationToken);
        LogTransitioned(logger, order.Id, target);
        return await DetailsAsync(order, cancellationToken);
    }

    public async Task<BulkActionResult> TransitionManyAsync(IReadOnlyList<Guid> ids, OrderStatus target, string? note, CancellationToken cancellationToken)
    {
        // One SaveChanges (one transaction) per order: a failure is reported for that order and
        // never rolls back the ones already done. The caller sees exactly which ones failed.
        var results = new List<BulkItemResult>(ids.Count);
        foreach (var id in ids.Distinct())
        {
            results.Add(await TryTransitionAsync(id, target, note, cancellationToken));
            db.ChangeTracker.Clear(); // a failed order must not be saved again by the next one
        }

        var succeeded = results.Count(r => r.Succeeded);
        LogBulk(logger, target, succeeded, results.Count - succeeded);
        return new BulkActionResult(succeeded, results.Count - succeeded, results);
    }

    private async Task<BulkItemResult> TryTransitionAsync(Guid id, OrderStatus target, string? note, CancellationToken cancellationToken)
    {
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return new BulkItemResult(id, false, null, BulkErrors.NotFound, "Order not found.");
        }

        if (!OrderLifecycle.CanMove(order.Status, target))
        {
            return new BulkItemResult(id, false, order.Status, BulkErrors.InvalidTransition, $"An order cannot move from {order.Status} to {target}.");
        }

        order.TransitionTo(target, request.UserId, clock.GetUtcNow(), note);
        try
        {
            await SaveOrConflictAsync(cancellationToken);
        }
        catch (ConflictException e)
        {
            return new BulkItemResult(id, false, null, BulkErrors.ConcurrentChange, e.Message);
        }

        return new BulkItemResult(id, true, order.Status, null, null);
    }

    /// <summary>Optimistic concurrency (xmin + history sequence) surfaces as one 409: reload and retry.</summary>
    private async Task SaveOrConflictAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ConcurrentChangeMessage);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} moved to {Status}")]
    private static partial void LogTransitioned(ILogger logger, Guid orderId, OrderStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bulk move to {Status}: {Succeeded} succeeded, {Failed} failed")]
    private static partial void LogBulk(ILogger logger, OrderStatus status, int succeeded, int failed);
}
