using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Finance;

namespace Hishab.Application.Finance;

/// <summary>A courier payout as the seller enters it from the courier's statement.</summary>
public sealed record RecordPayout(
    CourierProvider Provider,
    string Reference,
    DateOnly PaidOn,
    Money NetAmount,
    string? Note,
    IReadOnlyList<PayoutLineInput> Lines);

/// <summary>A recorded payout with its matched lines; <see cref="OrderNumbers"/> names the orders the lines found.</summary>
public sealed record PayoutDetails(Payout Payout, IReadOnlyDictionary<Guid, int> OrderNumbers);

public sealed record PayoutPage(IReadOnlyList<Payout> Items, int Page, int PageSize, int Total);

/// <summary>PRD "Record courier payouts and match them to parcels" (manual entry first, import later).</summary>
public interface IPayoutService
{
    /// <summary>
    /// Saves the payout and settles every matched order (Delivered -> CodSettled) in one transaction.
    /// Mismatched lines are saved with their reason and settle nothing.
    /// </summary>
    /// <exception cref="ConflictException">This courier's payout with this reference is already recorded, or a parcel was settled concurrently.</exception>
    Task<PayoutDetails> RecordAsync(RecordPayout payout, CancellationToken cancellationToken);

    /// <summary>
    /// Matches the payout's unknown and undelivered lines again against today's parcels and settles
    /// the orders they now match, in one transaction. Settled lines and amounts never change.
    /// </summary>
    /// <exception cref="NotFoundException">No such payout in this shop.</exception>
    /// <exception cref="ConflictException">An order or the payout was changed concurrently.</exception>
    Task<PayoutDetails> RematchAsync(Guid id, CancellationToken cancellationToken);

    /// <exception cref="NotFoundException">No such payout in this shop.</exception>
    Task<PayoutDetails> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Newest paid first, optionally for one courier.</summary>
    Task<PayoutPage> ListAsync(CourierProvider? provider, int page, int pageSize, CancellationToken cancellationToken);
}
