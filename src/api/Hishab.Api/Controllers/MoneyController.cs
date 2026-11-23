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
/// <param name="Collected">Paid out to the shop (COD settled), in BDT.</param>
/// <param name="Outstanding">Still with the courier, in BDT.</param>
/// <param name="OutstandingParcels">Parcels not paid out yet.</param>
/// <param name="OldestOutstandingDays">Age of the oldest unpaid parcel in days; null when nothing is owed.</param>
/// <param name="OutstandingByAge">Outstanding split by age: always four buckets, youngest first.</param>
public sealed record CourierCodDto(
    CourierProvider? Courier,
    int DeliveredParcels,
    decimal Expected,
    decimal Collected,
    decimal Outstanding,
    int OutstandingParcels,
    int? OldestOutstandingDays,
    IReadOnlyList<CodAgeBucketDto> OutstandingByAge);

/// <summary>What every courier owes the shop, and the totals.</summary>
public sealed record CodSummaryResponse(IReadOnlyList<CourierCodDto> Couriers, decimal Expected, decimal Collected, decimal Outstanding)
{
    public static CodSummaryResponse From(CodSummary summary) => new(
        [.. summary.Couriers.Select(c => new CourierCodDto(
            c.Courier,
            c.DeliveredParcels,
            c.Expected.Amount,
            c.Collected.Amount,
            c.Outstanding.Amount,
            c.OutstandingParcels,
            c.OldestOutstandingDays,
            [.. c.OutstandingByAge.Select(b => new CodAgeBucketDto(b.Age, b.Parcels, b.Amount.Amount))]))],
        summary.Expected.Amount,
        summary.Collected.Amount,
        summary.Outstanding.Amount);
}

/// <summary>The shop's money: cash on delivery, payouts and profit. Owner only (CanSeeProfit).</summary>
[ApiController]
[Route("money")]
[Authorize(Policy = Policies.CanSeeProfit)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class MoneyController(ICodSummaryService cod) : ControllerBase
{
    /// <summary>
    /// How much cash each courier owes the shop: COD expected on delivered parcels, collected
    /// (settled), and outstanding split by age in Dhaka days since delivery.
    /// </summary>
    [HttpGet("cod-summary")]
    [ProducesResponseType<CodSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CodSummaryResponse>> CodSummary(CancellationToken cancellationToken) =>
        Ok(CodSummaryResponse.From(await cod.GetAsync(cancellationToken)));
}
