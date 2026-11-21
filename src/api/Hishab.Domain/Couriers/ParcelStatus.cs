using Hishab.Domain.Orders;

namespace Hishab.Domain.Couriers;

/// <summary>A parcel's state as couriers report it, mapped by each adapter to these values.</summary>
public enum ParcelStatus
{
    /// <summary>Booked, waiting for pickup.</summary>
    Pending,
    PickedUp,
    InTransit,
    Delivered,
    Returned,
    Cancelled,

    /// <summary>A courier status the adapter has no mapping for: logged with the courier's text, never guessed.</summary>
    Unknown,
}

/// <summary>What a courier status means for the order: the moves to make, or a flag for staff.</summary>
/// <param name="Moves">Order statuses to move through, in order (empty: nothing to do).</param>
/// <param name="NeedsReview">The courier says something the order cannot follow (e.g. cancelled, or returned after delivered).</param>
public sealed record ParcelStatusDecision(IReadOnlyList<OrderStatus> Moves, bool NeedsReview)
{
    public static readonly ParcelStatusDecision Nothing = new([], false);
    public static readonly ParcelStatusDecision Review = new([], true);
}

/// <summary>
/// Turns a courier's parcel status into order moves, using only the lifecycle table. Safe to apply
/// again and again (a sync that runs twice, or a status seen before, changes nothing), and it
/// never skips a step: Delivered seen straight after Booked moves the order through InTransit first.
/// </summary>
public static class ParcelStatusRules
{
    /// <summary>The moves a courier is allowed to cause; later steps (COD settled, received back) are the shop's.</summary>
    private static readonly HashSet<(OrderStatus From, OrderStatus To)> _courierMoves =
    [
        (OrderStatus.Booked, OrderStatus.InTransit),
        (OrderStatus.InTransit, OrderStatus.Delivered),
        (OrderStatus.InTransit, OrderStatus.Returned),
    ];

    public static bool IsFinal(ParcelStatus status) =>
        status is ParcelStatus.Delivered or ParcelStatus.Returned or ParcelStatus.Cancelled;

    public static ParcelStatusDecision Decide(OrderStatus order, ParcelStatus parcel)
    {
        OrderStatus? target = parcel switch
        {
            ParcelStatus.Pending => OrderStatus.Booked,
            ParcelStatus.PickedUp or ParcelStatus.InTransit => OrderStatus.InTransit,
            ParcelStatus.Delivered => OrderStatus.Delivered,
            ParcelStatus.Returned => OrderStatus.Returned,
            _ => null, // Cancelled has no move in the PRD lifecycle; Unknown is never guessed
        };
        if (target is not { } to)
        {
            return ParcelStatusDecision.Review;
        }

        if (order == to || Reaches(to, order))
        {
            return ParcelStatusDecision.Nothing; // already there or past it: an old status, or a repeat
        }

        var path = PathTo(order, to);
        return path is null ? ParcelStatusDecision.Review : new ParcelStatusDecision(path, false);
    }

    /// <summary>Shortest chain of courier moves from <paramref name="from"/> to <paramref name="to"/>, or null.</summary>
    private static List<OrderStatus>? PathTo(OrderStatus from, OrderStatus to)
    {
        var previous = new Dictionary<OrderStatus, OrderStatus> { [from] = from };
        var queue = new Queue<OrderStatus>([from]);
        while (queue.TryDequeue(out var current))
        {
            foreach (var next in OrderLifecycle.AllowedNext(current).Where(n => _courierMoves.Contains((current, n))))
            {
                if (previous.TryAdd(next, current))
                {
                    queue.Enqueue(next);
                }
            }
        }

        if (!previous.ContainsKey(to))
        {
            return null;
        }

        var path = new List<OrderStatus>();
        for (var step = to; step != from; step = previous[step])
        {
            path.Add(step);
        }

        path.Reverse();
        return path;
    }

    /// <summary>Whether the full lifecycle leads from <paramref name="from"/> to <paramref name="to"/> (the order is already past it).</summary>
    private static bool Reaches(OrderStatus from, OrderStatus to)
    {
        var seen = new HashSet<OrderStatus> { from };
        var queue = new Queue<OrderStatus>([from]);
        while (queue.TryDequeue(out var current))
        {
            foreach (var next in OrderLifecycle.AllowedNext(current))
            {
                if (next == to)
                {
                    return true;
                }

                if (seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return false;
    }
}
