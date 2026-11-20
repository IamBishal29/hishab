namespace Hishab.Domain.Orders;

/// <summary>Where the order is in its life (PRD "Order lifecycle").</summary>
public enum OrderStatus
{
    New,
    Confirmed,
    Cancelled,
    Booked,
    InTransit,
    Delivered,
    Returned,
    CodSettled,
    ReceivedBack,
}

/// <summary>The channel the order came from, for "which channel sells best".</summary>
public enum OrderSource
{
    Facebook,
    Instagram,
    WhatsApp,
    Phone,
    WalkIn,
}

/// <summary>
/// The order lifecycle as data: the only moves allowed, copied from the PRD diagram. Every
/// status change goes through <see cref="Order.TransitionTo"/>, which consults this table, so
/// no service, bulk job or courier sync can skip a step.
/// </summary>
public static class OrderLifecycle
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> _next = new()
    {
        [OrderStatus.New] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Booked, OrderStatus.Cancelled],
        [OrderStatus.Booked] = [OrderStatus.InTransit],
        [OrderStatus.InTransit] = [OrderStatus.Delivered, OrderStatus.Returned],
        [OrderStatus.Delivered] = [OrderStatus.CodSettled],
        [OrderStatus.Returned] = [OrderStatus.ReceivedBack],
        [OrderStatus.CodSettled] = [],
        [OrderStatus.ReceivedBack] = [],
        [OrderStatus.Cancelled] = [],
    };

    public static IReadOnlyList<OrderStatus> AllowedNext(OrderStatus from) =>
        _next.TryGetValue(from, out var next) ? next : [];

    public static bool CanMove(OrderStatus from, OrderStatus to) => AllowedNext(from).Contains(to);

    /// <summary>No further moves: the order's story is over.</summary>
    public static bool IsFinal(OrderStatus status) => AllowedNext(status).Count == 0;
}
