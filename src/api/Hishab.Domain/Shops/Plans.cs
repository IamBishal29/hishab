using System.Globalization;
using Hishab.Domain.Common;

namespace Hishab.Domain.Shops;

/// <summary>Subscription plans (PRD "Business model and pricing").</summary>
public enum Plan
{
    Free,
    Starter,
    Growth,
    Business,
}

/// <summary>What a plan allows. Null means unlimited.</summary>
public sealed record PlanLimits(int? MaxOrdersPerMonth, int MaxUsers, int? MaxCouriers);

/// <summary>The plan catalogue: the entitlements of each plan, in one place.</summary>
public static class Plans
{
    private static readonly Dictionary<Plan, PlanLimits> _limits = new()
    {
        [Plan.Free] = new(MaxOrdersPerMonth: 100, MaxUsers: 1, MaxCouriers: 1),
        [Plan.Starter] = new(MaxOrdersPerMonth: 1_000, MaxUsers: 2, MaxCouriers: null), // "All couriers"
        [Plan.Growth] = new(MaxOrdersPerMonth: 5_000, MaxUsers: 5, MaxCouriers: null),
        [Plan.Business] = new(MaxOrdersPerMonth: null, MaxUsers: 15, MaxCouriers: null),
    };

    public static PlanLimits LimitsFor(Plan plan) =>
        _limits.TryGetValue(plan, out var limits) ? limits : throw new ArgumentOutOfRangeException(nameof(plan), plan, "Unknown plan.");
}

/// <summary>
/// A billing month in Bangladesh time (Asia/Dhaka, UTC+6, no daylight saving). Usage resets
/// at local midnight on the 1st, which is 18:00 UTC on the last day of the previous month.
/// </summary>
public sealed record BillingPeriod
{
    private static readonly TimeZoneInfo _dhaka = DhakaTime.Zone;

    private BillingPeriod(string key, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        Key = key;
        StartsAt = startsAt;
        EndsAt = endsAt;
    }

    /// <summary>"yyyy-MM" of the Dhaka month, e.g. "2026-11".</summary>
    public string Key { get; }

    /// <summary>Inclusive start, as a UTC instant.</summary>
    public DateTimeOffset StartsAt { get; }

    /// <summary>Exclusive end, as a UTC instant.</summary>
    public DateTimeOffset EndsAt { get; }

    public static BillingPeriod Containing(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, _dhaka);
        var firstOfMonth = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var startsAt = new DateTimeOffset(firstOfMonth, _dhaka.GetUtcOffset(firstOfMonth)).ToUniversalTime();
        var nextMonth = firstOfMonth.AddMonths(1);
        var endsAt = new DateTimeOffset(nextMonth, _dhaka.GetUtcOffset(nextMonth)).ToUniversalTime();
        return new BillingPeriod(firstOfMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture), startsAt, endsAt);
    }
}

public enum UsageMetric
{
    /// <summary>Orders created in the period. Never decremented: cancelling or deleting an order does not give the quota back.</summary>
    Orders,
}

/// <summary>A meter: how much of a metric a shop used in one billing period.</summary>
public sealed class UsageCounter : ITenantOwned
{
    public const int PeriodKeyLength = 7;

    private UsageCounter() => Period = null!;

    public UsageCounter(string period, UsageMetric metric, int count)
    {
        Period = period;
        Metric = metric;
        Count = count;
    }

    public Guid TenantId { get; set; }

    public string Period { get; private set; }

    public UsageMetric Metric { get; private set; }

    public int Count { get; private set; }
}

/// <summary>The plan does not allow more: the shop must upgrade (HTTP 402).</summary>
/// <param name="allowance">What the plan allows, ready to read: "1 user", "100 orders a month".</param>
public sealed class PlanLimitExceededException(Plan plan, string allowance, int limit)
    : DomainException($"Your {plan} plan allows {allowance}. Upgrade your plan to add more.")
{
    public static PlanLimitExceededException Users(Plan plan, int limit) =>
        new(plan, limit == 1 ? "1 user" : $"{limit} users", limit);

    public static PlanLimitExceededException Couriers(Plan plan, int limit) =>
        new(plan, limit == 1 ? "1 courier" : $"{limit} couriers", limit);

    public static PlanLimitExceededException Orders(Plan plan, int limit) =>
        new(plan, $"{limit} orders a month", limit);

    public Plan Plan { get; } = plan;

    public int Limit { get; } = limit;
}
