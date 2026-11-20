namespace Hishab.Domain.Common;

/// <summary>
/// Bangladesh time (Asia/Dhaka, UTC+6, no daylight saving). Sellers think in Dhaka days and
/// months; the database stores UTC. Convert at the edge with these helpers, never by hand.
/// </summary>
public static class DhakaTime
{
    public static TimeZoneInfo Zone { get; } = FindDhaka();

    /// <summary>Local midnight starting <paramref name="day"/>, as a UTC instant (18:00 UTC the day before).</summary>
    public static DateTimeOffset StartOfDay(DateOnly day)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(midnight, Zone.GetUtcOffset(midnight)).ToUniversalTime();
    }

    /// <summary>The Dhaka calendar day an instant falls on.</summary>
    public static DateOnly DayOf(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime);

    /// <summary>The OS time zone database when present; slim containers may lack it, and Dhaka is a fixed UTC+6.</summary>
    private static TimeZoneInfo FindDhaka() =>
        TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Dhaka", out var zone)
            ? zone
            : TimeZoneInfo.CreateCustomTimeZone("Asia/Dhaka", TimeSpan.FromHours(6), "Asia/Dhaka", "Bangladesh Standard Time");
}
