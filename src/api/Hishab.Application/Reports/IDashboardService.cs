using Hishab.Domain.Reports;

namespace Hishab.Application.Reports;

/// <summary>Today's pipeline. "Today" is the Dhaka day.</summary>
/// <param name="New">Orders taken today.</param>
/// <param name="ToBook">Confirmed orders waiting for a courier (any day).</param>
/// <param name="InTransit">Booked or in transit right now (any day).</param>
/// <param name="Delivered">Parcels delivered today.</param>
/// <param name="Returned">Parcels returned today.</param>
public sealed record TodayCounts(int New, int ToBook, int InTransit, int Delivered, int Returned);

public sealed record TodayDashboard(DateOnly Day, TodayCounts Counts, ProfitReport Profit);

/// <summary>The owner's numbers: computed on read from orders, shipments and payouts (no stored totals).</summary>
public interface IDashboardService
{
    Task<TodayDashboard> TodayAsync(CancellationToken cancellationToken);

    /// <summary>Profit per Dhaka day from <paramref name="first"/> to <paramref name="last"/> (inclusive) and the total.</summary>
    /// <exception cref="Domain.Common.DomainValidationException">An empty range or one longer than a year.</exception>
    Task<ProfitReport> ProfitAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken);
}
