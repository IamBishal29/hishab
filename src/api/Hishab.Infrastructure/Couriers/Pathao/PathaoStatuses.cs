using Hishab.Domain.Couriers;

namespace Hishab.Infrastructure.Couriers.Pathao;

/// <summary>
/// Pathao's order statuses mapped to ours. Anything not listed is <see cref="ParcelStatus.Unknown"/>:
/// the sync logs it with Pathao's text and leaves the order alone (never guess a delivery).
/// Partial delivery and exchange are deliberately Unknown: they need a person to settle the money.
/// </summary>
internal static class PathaoStatuses
{
    private static readonly Dictionary<string, ParcelStatus> _map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pending"] = ParcelStatus.Pending,
        ["Pickup_Requested"] = ParcelStatus.Pending,
        ["Assigned_for_Pickup"] = ParcelStatus.Pending,
        ["Pickup_Failed"] = ParcelStatus.Pending, // the rider tries again
        ["Picked"] = ParcelStatus.PickedUp,
        ["At_the_Sorting_HUB"] = ParcelStatus.InTransit,
        ["In_Transit"] = ParcelStatus.InTransit,
        ["Received_at_Last_Mile_HUB"] = ParcelStatus.InTransit,
        ["Assigned_for_Delivery"] = ParcelStatus.InTransit,
        ["Delivery_Failed"] = ParcelStatus.InTransit, // another attempt follows, or a return
        ["On_Hold"] = ParcelStatus.InTransit,
        ["Delivered"] = ParcelStatus.Delivered,
        ["Payment_Invoice"] = ParcelStatus.Delivered,
        ["Return"] = ParcelStatus.Returned,
        ["Paid_Return"] = ParcelStatus.Returned,
        ["Pickup_Cancelled"] = ParcelStatus.Cancelled,
    };

    /// <summary>Spaces and underscores are treated alike ("In Transit" = "In_Transit").</summary>
    public static ParcelStatus Map(string? status) =>
        status is not null && _map.TryGetValue(status.Trim().Replace(' ', '_'), out var mapped) ? mapped : ParcelStatus.Unknown;
}
