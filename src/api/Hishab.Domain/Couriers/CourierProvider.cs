namespace Hishab.Domain.Couriers;

/// <summary>Couriers Hishab has an adapter for (ICourierProvider, ADR 0005).</summary>
public enum CourierProvider
{
    /// <summary>In-process fake for development, tests and demos; never books a real parcel.</summary>
    Fake,
    Pathao,
    Steadfast,
}
