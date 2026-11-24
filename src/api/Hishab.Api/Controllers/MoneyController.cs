using Hishab.Api.Auth;
using Hishab.Application.Finance;
using Hishab.Domain.Couriers;
using Hishab.Domain.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hishab.Api.Controllers;

/// <summary>Unpaid cash that has been with the courier for this long (Dhaka days since delivery).</summary>
/// <param name="Age">Days0To3, Days4To7, Days8To14 or Days15Plus.</param>
/// <param name="Parcels">How many parcels.</param>
/// <param name="Amount">Their COD total, in BDT.</param>
public sealed record CodAgeBucketDto(CodAge Age, int Parcels, decimal Amount);

/// <summary>One courier's cash position. Expected = collected + outstanding.</summary>
/// <param name="Courier">The courier holding the cash; null for delivered orders without a booked shipment.</param>
/// <param name="DeliveredParcels">Delivered parcels (settled or not).</param>
/// <param name="Expected">COD due on every delivered parcel (order total minus advance), in BDT.</param>
/// <param name="Collected">What payouts actually paid for settled parcels, in BDT.</param>
/// <param name="Shortfall">Settled parcels' due minus what was paid (positive = short-paid), in BDT.</param>
/// <param name="ShortPaidParcels">Settled parcels paid a different amount than due.</param>
/// <param name="Outstanding">Still with the courier (not paid out yet), in BDT.</param>
/// <param name="OutstandingParcels">Parcels not paid out yet.</param>
/// <param name="OldestOutstandingDays">Age of the oldest unpaid parcel in days; null when nothing is owed.</param>
/// <param name="OutstandingByAge">Outstanding split by age: always four buckets, youngest first.</param>
public sealed record CourierCodDto(
    CourierProvider? Courier,
    int DeliveredParcels,
    decimal Expected,
    decimal Collected,
    decimal Shortfall,
    int ShortPaidParcels,
    decimal Outstanding,
    int OutstandingParcels,
    int? OldestOutstandingDays,
    IReadOnlyList<CodAgeBucketDto> OutstandingByAge);

/// <summary>What every courier owes the shop, and the totals. Expected = collected + shortfall + outstanding.</summary>
public sealed record CodSummaryResponse(IReadOnlyList<CourierCodDto> Couriers, decimal Expected, decimal Collected, decimal Shortfall, decimal Outstanding)
{
    public static CodSummaryResponse From(CodSummary summary) => new(
        [.. summary.Couriers.Select(c => new CourierCodDto(
            c.Courier,
            c.DeliveredParcels,
            c.Expected.Amount,
            c.Collected.Amount,
            c.Shortfall.Amount,
            c.ShortPaidParcels,
            c.Outstanding.Amount,
            c.OutstandingParcels,
            c.OldestOutstandingDays,
            [.. c.OutstandingByAge.Select(b => new CodAgeBucketDto(b.Age, b.Parcels, b.Amount.Amount))]))],
        summary.Expected.Amount,
        summary.Collected.Amount,
        summary.Shortfall.Amount,
        summary.Outstanding.Amount);
}

/// <summary>The shop's money: cash on delivery, payouts and profit. Owner only (CanSeeProfit).</summary>
[ApiController]
[Route("money")]
[Authorize(Policy = Policies.CanSeeProfit)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class MoneyController(ICodSummaryService cod, IPayoutService payouts) : ControllerBase
{
    /// <summary>
    /// How much cash each courier owes the shop: COD expected on delivered parcels, collected
    /// (settled), and outstanding split by age in Dhaka days since delivery.
    /// </summary>
    [HttpGet("cod-summary")]
    [ProducesResponseType<CodSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CodSummaryResponse>> CodSummary(CancellationToken cancellationToken) =>
        Ok(CodSummaryResponse.From(await cod.GetAsync(cancellationToken)));

    /// <summary>
    /// Records a courier payout from its statement and matches each line to a delivered parcel by
    /// consignment ID. Matched orders become CodSettled (a short or over payment still settles, with the
    /// gap shown); lines that match nothing are kept with the reason. One payout per courier and reference.
    /// </summary>
    [HttpPost("payouts")]
    [ProducesResponseType<PayoutResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PayoutResponse>> RecordPayout(RecordPayoutRequest request, CancellationToken cancellationToken)
    {
        var recorded = PayoutResponse.From(await payouts.RecordAsync(request.ToCommand(), cancellationToken));
        return Created($"/api/money/payouts/{recorded.Payout.Id}", recorded);
    }

    /// <summary>Recorded payouts, newest paid first.</summary>
    [HttpGet("payouts")]
    [ProducesResponseType<PayoutListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PayoutListResponse>> ListPayouts([FromQuery] PayoutListQuery query, CancellationToken cancellationToken)
    {
        var page = await payouts.ListAsync(query.Courier, query.Page, query.PageSize, cancellationToken);
        return Ok(new PayoutListResponse([.. page.Items.Select(PayoutSummaryDto.From)], page.Page, page.PageSize, page.Total));
    }

    /// <summary>
    /// Matches the payout's UnknownParcel and NotDelivered lines again (e.g. a parcel that was still in
    /// transit when the payout was entered and is delivered now) and settles the orders they now match.
    /// Settled lines and all amounts stay as they are. 409 if an order changed meanwhile (try again).
    /// </summary>
    [HttpPost("payouts/{id:guid}/rematch")]
    [ProducesResponseType<PayoutResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PayoutResponse>> RematchPayout(Guid id, CancellationToken cancellationToken) =>
        Ok(PayoutResponse.From(await payouts.RematchAsync(id, cancellationToken)));

    /// <summary>One payout with every statement line and what it matched.</summary>
    [HttpGet("payouts/{id:guid}")]
    [ProducesResponseType<PayoutResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PayoutResponse>> GetPayout(Guid id, CancellationToken cancellationToken) =>
        Ok(PayoutResponse.From(await payouts.GetAsync(id, cancellationToken)));
}
