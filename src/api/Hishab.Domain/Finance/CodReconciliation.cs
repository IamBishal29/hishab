using Hishab.Domain.Common;
using Hishab.Domain.Couriers;

namespace Hishab.Domain.Finance;

/// <summary>
/// One delivered parcel's cash: what the courier owes for it (the order's COD amount) and what a
/// payout actually paid (<see cref="Paid"/>; null while unpaid). <see cref="Courier"/> is null only
/// when a delivered order has no booked shipment (data repaired by hand): it is still owed, so it is kept.
/// </summary>
public sealed record CodParcel(CourierProvider? Courier, Money Due, DateTimeOffset DeliveredAt, Money? Paid)
{
    public bool Settled => Paid is not null;
}

/// <summary>How long outstanding cash has been with the courier, in Dhaka calendar days since delivery.</summary>
public enum CodAge
{
    /// <summary>0-3 days: normal; couriers pay out within a few working days.</summary>
    Days0To3,

    /// <summary>4-7 days: late; worth a look.</summary>
    Days4To7,

    /// <summary>8-14 days: overdue; chase the courier.</summary>
    Days8To14,

    /// <summary>15 days or more: at risk.</summary>
    Days15Plus,
}

public sealed record CodAgeBucket(CodAge Age, int Parcels, Money Amount);

/// <summary>
/// One courier's position: <see cref="Expected"/> = <see cref="Collected"/> + <see cref="Shortfall"/> +
/// <see cref="Outstanding"/>, always (no rounding anywhere: every amount is an exact sum of two-decimal
/// Money). Shortfall is what settled parcels were short-paid (negative when over-paid).
/// </summary>
public sealed record CourierCod(
    CourierProvider? Courier,
    int DeliveredParcels,
    Money Expected,
    Money Collected,
    Money Shortfall,
    int ShortPaidParcels,
    Money Outstanding,
    int OutstandingParcels,
    int? OldestOutstandingDays,
    IReadOnlyList<CodAgeBucket> OutstandingByAge);

public sealed record CodSummary(IReadOnlyList<CourierCod> Couriers, Money Expected, Money Collected, Money Shortfall, Money Outstanding);

/// <summary>
/// "Who owes me money": delivered parcels (expected) reconciled against what payouts paid (collected),
/// per courier, with the unpaid rest aged. Pure: the caller supplies the parcels and "today".
/// </summary>
public static class CodReconciliation
{
    public static CodSummary Summarize(IEnumerable<CodParcel> parcels, DateTimeOffset now)
    {
        var today = DhakaTime.DayOf(now);
        var couriers = parcels
            .GroupBy(p => p.Courier)
            .Select(g => ForCourier(g.Key, [.. g], today))
            // Couriers by name; the "no courier" group (null) last, where it stands out.
            .OrderBy(c => c.Courier is null)
            .ThenBy(c => c.Courier?.ToString(), StringComparer.Ordinal)
            .ToList();

        return new CodSummary(
            couriers,
            Sum(couriers.Select(c => c.Expected)),
            Sum(couriers.Select(c => c.Collected)),
            Sum(couriers.Select(c => c.Shortfall)),
            Sum(couriers.Select(c => c.Outstanding)));
    }

    /// <summary>Whole Dhaka days between delivery and today: delivered at 23:59 counts as one day old at 00:00.</summary>
    public static int AgeInDays(DateTimeOffset deliveredAt, DateOnly today) =>
        Math.Max(0, today.DayNumber - DhakaTime.DayOf(deliveredAt).DayNumber);

    public static CodAge AgeOf(int days) => days switch
    {
        <= 3 => CodAge.Days0To3,
        <= 7 => CodAge.Days4To7,
        <= 14 => CodAge.Days8To14,
        _ => CodAge.Days15Plus,
    };

    private static CourierCod ForCourier(CourierProvider? courier, IReadOnlyList<CodParcel> parcels, DateOnly today)
    {
        var paid = parcels.Where(p => p.Settled).Select(p => (p.Due, Paid: p.Paid!.Value)).ToList();
        var unpaid = parcels.Where(p => !p.Settled).Select(p => (p.Due, Days: AgeInDays(p.DeliveredAt, today))).ToList();

        // Every bucket is always present (zero when empty), so clients and charts get a fixed shape.
        var buckets = Enum.GetValues<CodAge>()
            .Select(age =>
            {
                var inBucket = unpaid.Where(u => AgeOf(u.Days) == age).ToList();
                return new CodAgeBucket(age, inBucket.Count, Sum(inBucket.Select(u => u.Due)));
            })
            .ToList();

        return new CourierCod(
            courier,
            parcels.Count,
            Expected: Sum(parcels.Select(p => p.Due)),
            Collected: Sum(paid.Select(p => p.Paid)),
            Shortfall: Sum(paid.Select(p => p.Due - p.Paid)),
            ShortPaidParcels: paid.Count(p => p.Paid != p.Due),
            Outstanding: Sum(unpaid.Select(u => u.Due)),
            OutstandingParcels: unpaid.Count,
            OldestOutstandingDays: unpaid.Count == 0 ? null : unpaid.Max(u => u.Days),
            OutstandingByAge: buckets);
    }

    private static Money Sum(IEnumerable<Money> amounts) => amounts.Aggregate(Money.Zero, (sum, m) => sum + m);
}
