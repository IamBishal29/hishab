using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Finance;
using Shouldly;

namespace Hishab.UnitTests.Finance;

public class CodReconciliationTests
{
    // 23 Nov 2026, 10:00 in Dhaka.
    private static readonly DateTimeOffset _now = new(2026, 11, 23, 4, 0, 0, TimeSpan.Zero);

    private static CodParcel Parcel(CourierProvider? courier, decimal due, int daysAgo, bool settled = false) =>
        new(courier, Money.Bdt(due), _now.AddDays(-daysAgo), settled);

    [Fact]
    public void Expected_is_collected_plus_outstanding_per_courier_and_in_total()
    {
        var summary = CodReconciliation.Summarize(
            [
                Parcel(CourierProvider.Pathao, 1360.50m, 1),
                Parcel(CourierProvider.Pathao, 990m, 2, settled: true),
                Parcel(CourierProvider.Fake, 450.25m, 5),
            ],
            _now);

        var pathao = summary.Couriers.Single(c => c.Courier == CourierProvider.Pathao);
        pathao.Expected.ShouldBe(Money.Bdt(2350.50m));
        pathao.Collected.ShouldBe(Money.Bdt(990m));
        pathao.Outstanding.ShouldBe(Money.Bdt(1360.50m));
        pathao.DeliveredParcels.ShouldBe(2);
        pathao.OutstandingParcels.ShouldBe(1);

        summary.Expected.ShouldBe(Money.Bdt(2800.75m));
        summary.Collected.ShouldBe(Money.Bdt(990m));
        summary.Outstanding.ShouldBe(Money.Bdt(1810.75m));
        (summary.Collected + summary.Outstanding).ShouldBe(summary.Expected);
    }

    [Fact]
    public void Amounts_add_up_exactly_without_floating_point_drift()
    {
        // 0.1 + 0.2 is not 0.3 in double; decimal Money must be exact over many parcels.
        var parcels = Enumerable.Range(0, 1000).Select(_ => Parcel(CourierProvider.Fake, 0.10m, 1));

        CodReconciliation.Summarize(parcels, _now).Outstanding.ShouldBe(Money.Bdt(100.00m));
    }

    [Theory]
    [InlineData(0, CodAge.Days0To3)]
    [InlineData(3, CodAge.Days0To3)]
    [InlineData(4, CodAge.Days4To7)]
    [InlineData(7, CodAge.Days4To7)]
    [InlineData(8, CodAge.Days8To14)]
    [InlineData(14, CodAge.Days8To14)]
    [InlineData(15, CodAge.Days15Plus)]
    [InlineData(90, CodAge.Days15Plus)]
    public void Ages_fall_into_fixed_buckets(int days, CodAge expected) =>
        CodReconciliation.AgeOf(days).ShouldBe(expected);

    [Fact]
    public void Age_counts_Dhaka_calendar_days_not_24_hour_periods()
    {
        // Now = 10:00 on 23 Nov in Dhaka. 23:30 on the 22nd in Dhaka (17:30 UTC) was yesterday: 1 day,
        // though under 24 hours ago. 00:30 on the 23rd in Dhaka (18:30 UTC on the 22nd, "yesterday"
        // by a UTC date) is today: 0 days.
        var today = DhakaTime.DayOf(_now);

        CodReconciliation.AgeInDays(new DateTimeOffset(2026, 11, 22, 17, 30, 0, TimeSpan.Zero), today).ShouldBe(1);
        CodReconciliation.AgeInDays(new DateTimeOffset(2026, 11, 22, 18, 30, 0, TimeSpan.Zero), today).ShouldBe(0);
    }

    [Fact]
    public void Outstanding_is_split_by_age_with_every_bucket_present()
    {
        var summary = CodReconciliation.Summarize(
            [
                Parcel(CourierProvider.Fake, 100m, 1),
                Parcel(CourierProvider.Fake, 200m, 2),
                Parcel(CourierProvider.Fake, 300m, 20),
                Parcel(CourierProvider.Fake, 999m, 30, settled: true),
            ],
            _now);

        var fake = summary.Couriers.ShouldHaveSingleItem();
        fake.OutstandingByAge.Select(b => (b.Age, b.Parcels, b.Amount.Amount)).ShouldBe(
        [
            (CodAge.Days0To3, 2, 300m),
            (CodAge.Days4To7, 0, 0m),
            (CodAge.Days8To14, 0, 0m),
            (CodAge.Days15Plus, 1, 300m),
        ]);
        fake.OldestOutstandingDays.ShouldBe(20); // the settled 30-day parcel is not owed
    }

    [Fact]
    public void A_courier_that_paid_everything_owes_nothing()
    {
        var summary = CodReconciliation.Summarize([Parcel(CourierProvider.Pathao, 500m, 9, settled: true)], _now);

        var pathao = summary.Couriers.ShouldHaveSingleItem();
        pathao.Outstanding.ShouldBe(Money.Zero);
        pathao.OldestOutstandingDays.ShouldBeNull();
        pathao.OutstandingByAge.ShouldAllBe(b => b.Parcels == 0);
    }

    [Fact]
    public void Delivered_orders_without_a_courier_are_still_owed_and_listed_last()
    {
        var summary = CodReconciliation.Summarize(
            [Parcel(null, 300m, 1), Parcel(CourierProvider.Pathao, 100m, 1), Parcel(CourierProvider.Fake, 100m, 1)],
            _now);

        summary.Couriers.Select(c => c.Courier).ShouldBe([CourierProvider.Fake, CourierProvider.Pathao, null]);
        summary.Outstanding.ShouldBe(Money.Bdt(500m));
    }

    [Fact]
    public void No_deliveries_means_an_empty_summary()
    {
        var summary = CodReconciliation.Summarize([], _now);

        summary.Couriers.ShouldBeEmpty();
        summary.Expected.ShouldBe(Money.Zero);
    }
}
