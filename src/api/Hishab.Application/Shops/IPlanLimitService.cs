using Hishab.Domain.Shops;

namespace Hishab.Application.Shops;

/// <summary>
/// Enforces the current shop's plan on the server, where clients cannot skip it. Every write
/// that consumes quota goes through here.
/// </summary>
public interface IPlanLimitService
{
    /// <summary>
    /// Counts one order against this month's quota, atomically: check and increment are one
    /// statement, so parallel requests at the limit cannot both pass. Call it inside the
    /// transaction that creates the order, so a failed order also gives the quota back.
    /// </summary>
    /// <exception cref="PlanLimitExceededException">The monthly order limit is reached.</exception>
    Task ReserveOrderAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Locks the shop row and checks the user limit. Must run inside the transaction that adds
    /// the user, so concurrent invites are checked one after another.
    /// </summary>
    /// <exception cref="PlanLimitExceededException">The plan's user limit is reached.</exception>
    Task EnsureCanAddStaffAsync(CancellationToken cancellationToken);

    Task<ShopUsage> GetUsageAsync(CancellationToken cancellationToken);
}

/// <summary>Usage of the current billing period (Dhaka calendar month).</summary>
public sealed record ShopUsage(Plan Plan, string Period, DateTimeOffset PeriodStartsAt, DateTimeOffset PeriodEndsAt, UsageMeter Orders, UsageMeter Users);

/// <summary><see cref="Limit"/> null means unlimited.</summary>
public sealed record UsageMeter(int Used, int? Limit);
