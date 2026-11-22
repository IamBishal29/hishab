using Hishab.Domain.Common;

namespace Hishab.Domain.Couriers;

/// <summary>The levels of a courier's own location list (Pathao: city → zone → area).</summary>
public enum LocationLevel
{
    City,
    Zone,
    Area,
}

/// <summary>
/// Where a parcel goes in one courier's own location IDs (Pathao prices and routes by them). The
/// IDs mean nothing to another courier, so they live on the <see cref="Shipment"/>, not the order.
/// Kept as text: Pathao uses numbers, other couriers use codes. Area is optional (Pathao).
/// </summary>
public sealed record CourierDestination
{
    public const int IdMaxLength = 32;

    public CourierDestination(string cityId, string zoneId, string? areaId = null)
    {
        CityId = Check(cityId, "cityId") ?? throw Missing("cityId", "city");
        ZoneId = Check(zoneId, "zoneId") ?? throw Missing("zoneId", "zone");
        AreaId = Check(areaId, "areaId");
    }

    public string CityId { get; }

    public string ZoneId { get; }

    public string? AreaId { get; }

    private static string? Check(string? id, string field)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return id.Length > IdMaxLength || id.Any(char.IsWhiteSpace)
            ? throw new DomainValidationException($"destination.{field}", "This is not a courier location ID.")
            : id;
    }

    private static DomainValidationException Missing(string field, string what) =>
        new($"destination.{field}", $"Choose the delivery {what}.");
}
