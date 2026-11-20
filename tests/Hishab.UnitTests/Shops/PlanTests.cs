using Hishab.Domain.Shops;

namespace Hishab.UnitTests.Shops;

public class PlanTests
{
    [Theory]
    [InlineData(Plan.Free, 100, 1)]
    [InlineData(Plan.Starter, 1_000, 2)]
    [InlineData(Plan.Growth, 5_000, 5)]
    [InlineData(Plan.Business, null, 15)]
    public void Plan_limits_match_the_PRD(Plan plan, int? orders, int users)
    {
        Plans.LimitsFor(plan).ShouldBe(new PlanLimits(orders, users));
    }

    [Fact]
    public void New_shops_start_on_the_free_plan()
    {
        Shop.Create(DateTimeOffset.UtcNow).Plan.ShouldBe(Plan.Free);
    }

    [Fact]
    public void Unknown_plan_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Shop.Create(DateTimeOffset.UtcNow).ChangePlan((Plan)42));
    }
}

public class BillingPeriodTests
{
    private static DateTimeOffset Utc(string iso) => DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2026-11-30T17:59:59Z", "2026-11")] // 23:59:59 in Dhaka, still November
    [InlineData("2026-11-30T18:00:00Z", "2026-12")] // midnight in Dhaka: December starts
    [InlineData("2026-12-31T18:00:00Z", "2027-01")] // year boundary in Dhaka, still 2026 in UTC
    [InlineData("2026-11-01T00:00:00Z", "2026-11")]
    public void Month_follows_the_Dhaka_calendar_not_UTC(string instant, string expected)
    {
        BillingPeriod.Containing(Utc(instant)).Key.ShouldBe(expected);
    }

    [Fact]
    public void Period_bounds_are_Dhaka_midnights_as_UTC_instants()
    {
        var period = BillingPeriod.Containing(Utc("2026-11-20T08:00:00Z"));

        period.StartsAt.ShouldBe(Utc("2026-10-31T18:00:00Z"));
        period.EndsAt.ShouldBe(Utc("2026-11-30T18:00:00Z"));
        period.StartsAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Consecutive_periods_touch_without_gap_or_overlap()
    {
        var november = BillingPeriod.Containing(Utc("2026-11-15T00:00:00Z"));

        BillingPeriod.Containing(november.EndsAt).StartsAt.ShouldBe(november.EndsAt);
        BillingPeriod.Containing(november.EndsAt.AddTicks(-1)).Key.ShouldBe("2026-11");
    }

    [Fact]
    public void February_in_a_leap_year_ends_on_the_29th_in_Dhaka()
    {
        BillingPeriod.Containing(Utc("2028-02-10T00:00:00Z")).EndsAt.ShouldBe(Utc("2028-02-29T18:00:00Z"));
    }
}
