namespace Hishab.Domain.Customers;

/// <summary>How careful to be before shipping to this buyer. Ordered from least to most careful (New aside).</summary>
public enum CustomerRiskLevel
{
    /// <summary>No delivered or returned parcel yet: nothing to judge by (most fake orders come from here).</summary>
    New,

    /// <summary>Takes their parcels.</summary>
    Safe,

    /// <summary>Some returns or cancellations: confirm by phone first.</summary>
    Watch,

    /// <summary>Blocked, or returns parcels often: ask for the delivery charge in advance.</summary>
    Risky,
}

/// <summary>Why a level was given; every reason that applies is listed, so the badge can be explained.</summary>
public enum RiskReason
{
    /// <summary>The shop blocked this customer.</summary>
    Blocked,

    /// <summary>At least <see cref="RiskThresholds.RiskyMinReturns"/> returns and a return rate of at least <see cref="RiskThresholds.RiskyReturnRate"/>.</summary>
    HighReturnRate,

    /// <summary>A return rate of at least <see cref="RiskThresholds.WatchReturnRate"/>.</summary>
    ElevatedReturnRate,

    /// <summary>Returned a parcel in the last <see cref="RiskThresholds.RecentReturnDays"/> days, and nothing delivered since.</summary>
    RecentReturn,

    /// <summary>At least <see cref="RiskThresholds.WatchCancellations"/> cancelled orders.</summary>
    ManyCancellations,

    /// <summary>No delivered or returned parcel yet.</summary>
    NoHistory,
}

/// <summary>The customer's record with this shop only (the shared network is V2).</summary>
/// <param name="Delivered">Delivered and COD-settled orders.</param>
/// <param name="Returned">Returned and received-back orders.</param>
/// <param name="LastDeliveredAt">When the latest parcel was delivered; null if never.</param>
/// <param name="LastReturnedAt">When the latest parcel was returned; null if never.</param>
public sealed record RiskHistory(
    bool IsBlocked,
    int Delivered,
    int Returned,
    int Cancelled,
    DateTimeOffset? LastDeliveredAt,
    DateTimeOffset? LastReturnedAt);

/// <summary>The tunable numbers behind the rules. Rates are fractions of finished parcels (delivered + returned).</summary>
public sealed record RiskThresholds(
    int RiskyMinReturns = 2,
    decimal RiskyReturnRate = 0.5m,
    decimal WatchReturnRate = 0.3m,
    int RecentReturnDays = 90,
    int WatchCancellations = 3)
{
    public static RiskThresholds Default { get; } = new();

    public bool IsValid =>
        RiskyMinReturns >= 1
        && WatchReturnRate is > 0m and <= 1m
        && RiskyReturnRate >= WatchReturnRate && RiskyReturnRate <= 1m
        && RecentReturnDays >= 1
        && WatchCancellations >= 1;
}

/// <param name="ReturnRate">Returned / (delivered + returned), 0-1 rounded to 2 places; null with no finished parcels.</param>
public sealed record RiskAssessment(CustomerRiskLevel Level, IReadOnlyList<RiskReason> Reasons, decimal? ReturnRate);

/// <summary>
/// Explainable rules, not a model: a seller can read why a buyer is Watch or Risky and disagree.
/// Pure: the caller supplies the history, the thresholds and "now".
/// </summary>
public static class CustomerRisk
{
    public static RiskAssessment Assess(RiskHistory history, RiskThresholds thresholds, DateTimeOffset now)
    {
        var finished = history.Delivered + history.Returned;
        decimal? rate = finished == 0 ? null : (decimal)history.Returned / finished;
        var reasons = new List<RiskReason>();

        if (history.IsBlocked)
        {
            reasons.Add(RiskReason.Blocked);
        }

        if (history.Returned >= thresholds.RiskyMinReturns && rate >= thresholds.RiskyReturnRate)
        {
            reasons.Add(RiskReason.HighReturnRate);
        }
        else if (history.Returned > 0 && rate >= thresholds.WatchReturnRate)
        {
            reasons.Add(RiskReason.ElevatedReturnRate);
        }

        // A return followed by a delivery is forgiven early: the buyer took a parcel since.
        if (history.LastReturnedAt is { } returnedAt
            && now - returnedAt <= TimeSpan.FromDays(thresholds.RecentReturnDays)
            && !(history.LastDeliveredAt > returnedAt))
        {
            reasons.Add(RiskReason.RecentReturn);
        }

        if (history.Cancelled >= thresholds.WatchCancellations)
        {
            reasons.Add(RiskReason.ManyCancellations);
        }

        if (finished == 0)
        {
            reasons.Add(RiskReason.NoHistory);
        }

        return new RiskAssessment(LevelFor(reasons), reasons, rate is null ? null : decimal.Round(rate.Value, 2));
    }

    private static CustomerRiskLevel LevelFor(List<RiskReason> reasons) =>
        reasons.Contains(RiskReason.Blocked) || reasons.Contains(RiskReason.HighReturnRate) ? CustomerRiskLevel.Risky
        : reasons.Exists(r => r is RiskReason.ElevatedReturnRate or RiskReason.RecentReturn or RiskReason.ManyCancellations) ? CustomerRiskLevel.Watch
        : reasons.Contains(RiskReason.NoHistory) ? CustomerRiskLevel.New
        : CustomerRiskLevel.Safe;
}
