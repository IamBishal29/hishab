using Hishab.Domain.Finance;

namespace Hishab.Application.Finance;

/// <summary>How much cash each courier owes the shop (PRD "COD expected vs collected per courier").</summary>
public interface ICodSummaryService
{
    /// <summary>
    /// Every delivered parcel of this shop: expected (COD due), collected (what payouts paid),
    /// shortfall (settled but short-paid) and the unpaid rest aged in Dhaka days.
    /// </summary>
    Task<CodSummary> GetAsync(CancellationToken cancellationToken);
}
