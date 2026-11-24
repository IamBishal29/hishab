using FluentValidation;
using Hishab.Api.Auth;
using Hishab.Application.Reports;
using Hishab.Domain.Common;
using Hishab.Domain.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hishab.Api.Controllers;

/// <summary>Real profit = sales - productCost - deliveryFees - returnCharges - codFees (all BDT).</summary>
/// <param name="Delivered">Parcels delivered (sales count on the delivery day).</param>
/// <param name="Returned">Parcels returned (on the return day).</param>
/// <param name="Sales">Order totals of delivered parcels: lines + delivery charge, advance included.</param>
/// <param name="ProductCost">Cost price of the sold lines (lines without one count 0).</param>
/// <param name="DeliveryFees">The courier's delivery fees: from its payout when paid, else the booking quote.</param>
/// <param name="ReturnCharges">The courier's charges for bringing returned parcels back.</param>
/// <param name="CodFees">The courier's cash-handling fees, known once a payout settles the parcel.</param>
/// <param name="Profit">What the shop keeps.</param>
public sealed record ProfitFiguresDto(
    int Delivered,
    int Returned,
    decimal Sales,
    decimal ProductCost,
    decimal DeliveryFees,
    decimal ReturnCharges,
    decimal CodFees,
    decimal Profit)
{
    internal static ProfitFiguresDto From(ProfitFigures f) => new(
        f.Delivered, f.Returned, f.Sales.Amount, f.ProductCost.Amount, f.DeliveryFees.Amount, f.ReturnCharges.Amount, f.CodFees.Amount, f.Profit.Amount);
}

/// <summary>One Dhaka day.</summary>
public sealed record ProfitDayDto(DateOnly Day, ProfitFiguresDto Figures);

/// <summary>Profit per Dhaka day and in total.</summary>
/// <param name="From">First day of the range (Dhaka).</param>
/// <param name="To">Last day of the range (Dhaka), inclusive.</param>
/// <param name="Days">Every day in the range, oldest first, including days with nothing.</param>
/// <param name="Total">The whole range.</param>
/// <param name="Missing">Figures that were unknown and counted as 0: when <c>missing.any</c>, profit may be overstated.</param>
public sealed record ProfitReportResponse(DateOnly From, DateOnly To, IReadOnlyList<ProfitDayDto> Days, ProfitFiguresDto Total, ProfitMissing Missing)
{
    internal static ProfitReportResponse Of(ProfitReport r) =>
        new(r.From, r.To, [.. r.Days.Select(d => new ProfitDayDto(d.Day, ProfitFiguresDto.From(d.Figures)))], ProfitFiguresDto.From(r.Total), r.Missing);
}

/// <summary>The owner's home screen: today's pipeline and today's profit.</summary>
/// <param name="Day">Today in Dhaka.</param>
/// <param name="Counts">Today's pipeline.</param>
/// <param name="Profit">Today's profit.</param>
public sealed record TodayResponse(DateOnly Day, TodayCounts Counts, ProfitReportResponse Profit);

/// <summary>A range of Dhaka days; both empty = the current month so far.</summary>
public sealed class ProfitQuery
{
    /// <summary>First day (inclusive). <example>2026-11-01</example></summary>
    public DateOnly? From { get; init; }

    /// <summary>Last day (inclusive), at most 366 days after <c>from</c>. <example>2026-11-30</example></summary>
    public DateOnly? To { get; init; }
}

public sealed class ProfitQueryValidator : AbstractValidator<ProfitQuery>
{
    public ProfitQueryValidator()
    {
        RuleFor(q => q.From).NotNull().When(q => q.To is not null).WithMessage("Give both from and to, or neither.");
        RuleFor(q => q.To).NotNull().When(q => q.From is not null).WithMessage("Give both from and to, or neither.");
        RuleFor(q => q.To)
            .Must((q, to) => to >= q.From && to!.Value.DayNumber - q.From!.Value.DayNumber < ProfitReport.MaxDays)
            .When(q => q.From is not null && q.To is not null)
            .WithMessage($"Choose a range of 1 to {ProfitReport.MaxDays} days.");
    }
}

/// <summary>Owner reports: pipeline counts and real profit. Owner only (CanSeeProfit).</summary>
[ApiController]
[Route("reports")]
[Authorize(Policy = Policies.CanSeeProfit)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class ReportsController(IDashboardService dashboard, TimeProvider clock) : ControllerBase
{
    /// <summary>
    /// Today (Dhaka): orders taken, waiting to book, on the road, delivered and returned, and today's
    /// profit.
    /// </summary>
    [HttpGet("today")]
    [ProducesResponseType<TodayResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TodayResponse>> Today(CancellationToken cancellationToken)
    {
        var today = await dashboard.TodayAsync(cancellationToken);
        return Ok(new TodayResponse(today.Day, today.Counts, ProfitReportResponse.Of(today.Profit)));
    }

    /// <summary>
    /// Real profit per Dhaka day and in total for a range (default: this month so far). Sales count
    /// on the delivery day, returns on the return day. Unknown figures count 0 and are listed in
    /// <c>missing</c>.
    /// </summary>
    [HttpGet("profit")]
    [ProducesResponseType<ProfitReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProfitReportResponse>> Profit([FromQuery] ProfitQuery query, CancellationToken cancellationToken)
    {
        var today = DhakaTime.DayOf(clock.GetUtcNow());
        var first = query.From ?? new DateOnly(today.Year, today.Month, 1);
        var last = query.To ?? today;
        return Ok(ProfitReportResponse.Of(await dashboard.ProfitAsync(first, last, cancellationToken)));
    }
}
