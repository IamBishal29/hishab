using Hishab.Application.Customers;
using Hishab.Application.Orders;
using Hishab.Domain.Customers;
using Microsoft.Extensions.Options;

namespace Hishab.Infrastructure.Customers;

/// <summary>The same rules for every shop (Syed's choice); thresholds from configuration, checked at startup.</summary>
public sealed class RiskOptions
{
    public const string SectionName = "Risk";

    public int RiskyMinReturns { get; set; } = RiskThresholds.Default.RiskyMinReturns;

    public decimal RiskyReturnRate { get; set; } = RiskThresholds.Default.RiskyReturnRate;

    public decimal WatchReturnRate { get; set; } = RiskThresholds.Default.WatchReturnRate;

    public int RecentReturnDays { get; set; } = RiskThresholds.Default.RecentReturnDays;

    public int WatchCancellations { get; set; } = RiskThresholds.Default.WatchCancellations;

    public RiskThresholds ToThresholds() => new(RiskyMinReturns, RiskyReturnRate, WatchReturnRate, RecentReturnDays, WatchCancellations);
}

internal sealed class CustomerRiskAssessor(IOptions<RiskOptions> options, TimeProvider clock) : ICustomerRiskAssessor
{
    private readonly RiskThresholds _thresholds = options.Value.ToThresholds();

    public RiskAssessment Assess(CustomerOrderHistory history, bool isBlocked) =>
        CustomerRisk.Assess(
            new RiskHistory(isBlocked, history.Delivered, history.Returned, history.Cancelled, history.LastDeliveredAt, history.LastReturnedAt),
            _thresholds,
            clock.GetUtcNow());
}
