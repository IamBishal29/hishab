using Hishab.Domain.Finance;

namespace Hishab.Application.Finance;

/// <summary>How much cash each courier owes the shop (PRD "COD expected vs collected per courier").</summary>
public interface ICodSummaryService
{
    /// <summary>
    /// Every delivered parcel of this shop: expected (COD due), collected (settled) and the unpaid
    /// rest aged in Dhaka days. Collected = orders in CodSettled until payouts are recorded (#28).
    /// </summary>
    Task<CodSummary> GetAsync(CancellationToken cancellationToken);
}
