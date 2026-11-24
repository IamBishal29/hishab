using Hishab.Domain.Customers;
using Shouldly;

namespace Hishab.UnitTests.Customers;

public class CustomerRiskTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 24, 4, 0, 0, TimeSpan.Zero);

    private static RiskAssessment Assess(
        int delivered = 0,
        int returned = 0,
        int cancelled = 0,
        bool blocked = false,
        int? lastDeliveredDaysAgo = null,
        int? lastReturnedDaysAgo = null) =>
        CustomerRisk.Assess(
            new RiskHistory(blocked, delivered, returned, cancelled, Ago(lastDeliveredDaysAgo), Ago(lastReturnedDaysAgo)),
            RiskThresholds.Default,
            _now);

    private static DateTimeOffset? Ago(int? days) => days is null ? null : _now.AddDays(-days.Value);

    [Fact]
    public void A_new_customer_is_New_not_Safe()
    {
        var risk = Assess();

        risk.Level.ShouldBe(CustomerRiskLevel.New);
        risk.Reasons.ShouldBe([RiskReason.NoHistory]);
        risk.ReturnRate.ShouldBeNull();
    }

    [Fact]
    public void Many_deliveries_and_no_returns_is_Safe()
    {
        var risk = Assess(delivered: 25, lastDeliveredDaysAgo: 3);

        risk.Level.ShouldBe(CustomerRiskLevel.Safe);
        risk.Reasons.ShouldBeEmpty();
        risk.ReturnRate.ShouldBe(0m);
    }

    [Fact]
    public void A_single_recent_return_is_Watch_not_Risky()
    {
        var risk = Assess(returned: 1, lastReturnedDaysAgo: 10);

        risk.Level.ShouldBe(CustomerRiskLevel.Watch);
        risk.Reasons.ShouldBe([RiskReason.ElevatedReturnRate, RiskReason.RecentReturn]);
        risk.ReturnRate.ShouldBe(1m);
    }

    [Fact]
    public void One_old_return_among_many_deliveries_is_Safe()
    {
        Assess(delivered: 20, returned: 1, lastDeliveredDaysAgo: 5, lastReturnedDaysAgo: 200).Level.ShouldBe(CustomerRiskLevel.Safe);
    }

    [Fact]
    public void A_delivery_after_a_recent_return_forgives_it()
    {
        var risk = Assess(delivered: 9, returned: 1, lastDeliveredDaysAgo: 2, lastReturnedDaysAgo: 20);

        risk.Level.ShouldBe(CustomerRiskLevel.Safe);
        risk.Reasons.ShouldNotContain(RiskReason.RecentReturn);
    }

    [Theory]
    [InlineData(2, 2)] // 50% with two returns: exactly at the Risky line
    [InlineData(0, 2)]
    [InlineData(3, 4)]
    public void Two_or_more_returns_at_half_or_more_is_Risky(int delivered, int returned)
    {
        var risk = Assess(delivered: delivered, returned: returned, lastReturnedDaysAgo: 400);

        risk.Level.ShouldBe(CustomerRiskLevel.Risky);
        risk.Reasons.ShouldBe([RiskReason.HighReturnRate]);
    }

    [Fact]
    public void Two_returns_among_many_deliveries_is_only_Watch_by_rate_threshold()
    {
        // 2 of 6 = 33%: above the Watch rate, below the Risky rate.
        Assess(delivered: 4, returned: 2, lastDeliveredDaysAgo: 1, lastReturnedDaysAgo: 30).Level.ShouldBe(CustomerRiskLevel.Watch);
    }

    [Fact]
    public void Many_cancellations_put_even_a_new_customer_on_Watch()
    {
        var risk = Assess(cancelled: 3);

        risk.Level.ShouldBe(CustomerRiskLevel.Watch);
        risk.Reasons.ShouldBe([RiskReason.ManyCancellations, RiskReason.NoHistory]);
    }

    [Fact]
    public void A_blocked_customer_is_always_Risky_even_with_a_perfect_record()
    {
        var risk = Assess(delivered: 50, blocked: true, lastDeliveredDaysAgo: 1);

        risk.Level.ShouldBe(CustomerRiskLevel.Risky);
        risk.Reasons.ShouldBe([RiskReason.Blocked]);
    }

    [Fact]
    public void Thresholds_are_configurable()
    {
        var strict = new RiskThresholds(RiskyMinReturns: 1, RiskyReturnRate: 0.1m, WatchReturnRate: 0.05m);

        CustomerRisk.Assess(new RiskHistory(false, 9, 1, 0, null, null), strict, _now).Level.ShouldBe(CustomerRiskLevel.Risky);
    }

    [Theory]
    [InlineData(0, 0.5, 0.3)]   // no returns needed for Risky
    [InlineData(2, 0.2, 0.3)]   // Risky rate below Watch rate
    [InlineData(2, 0.5, 0)]     // Watch rate must be above zero
    public void Inconsistent_thresholds_are_invalid(int minReturns, double riskyRate, double watchRate)
    {
        new RiskThresholds(minReturns, (decimal)riskyRate, (decimal)watchRate).IsValid.ShouldBeFalse();
    }
}
