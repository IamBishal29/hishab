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
}
