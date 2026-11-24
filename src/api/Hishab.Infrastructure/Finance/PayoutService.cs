using Hishab.Application.Common.Auditing;
using Hishab.Application.Finance;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Finance;
using Hishab.Domain.Orders;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hishab.Infrastructure.Finance;

/// <summary>
/// Records a payout in one SaveChanges (one transaction): the payout, its matched lines and every
/// settled order's Delivered -> CodSettled step. Either all of it is saved or none, so the books
/// never show a payout without its settlements, or settled orders without the payout that paid them.
/// Recording the same statement twice is refused twice over: a pre-check for a clear message and a
/// unique index for the race; a parcel can only be settled once (unique index on the settling line).
/// </summary>
internal sealed partial class PayoutService(
    HishabDbContext db,
    IRequestContext request,
    TimeProvider clock,
    ILogger<PayoutService> logger) : IPayoutService
{
    internal const string ReferenceIndex = "ux_payouts_provider_reference";
    internal const string SettledShipmentIndex = "ux_payout_lines_settled_shipment";

    public async Task<PayoutDetails> RecordAsync(RecordPayout command, CancellationToken cancellationToken)
    {
        var reference = command.Reference?.Trim() ?? "";
        if (await db.Payouts.AnyAsync(p => p.Provider == command.Provider && p.Reference == reference, cancellationToken))
        {
            throw Duplicate(command.Provider, reference);
        }

        var consignments = command.Lines.Select(l => l.ConsignmentId?.Trim() ?? "").Where(c => c.Length > 0).Distinct().ToList();
        var shipments = await db.Shipments.AsNoTracking()
            .Where(s => s.Provider == command.Provider && s.State == ShipmentState.Booked && consignments.Contains(s.ConsignmentId!))
            .Select(s => new { s.Id, s.OrderId, ConsignmentId = s.ConsignmentId! })
            .ToListAsync(cancellationToken);

        // Tracked: settled orders change status in the same SaveChanges as the payout.
        var orderIds = shipments.Select(s => s.OrderId).ToList();
        var orders = await db.Orders.AsSplitQuery().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);

        var parcels = shipments
            .DistinctBy(s => s.ConsignmentId) // couriers do not reuse IDs; if one ever did, the first parcel wins
            .ToDictionary(
                s => s.ConsignmentId,
                s => new PayoutParcel(s.Id, s.OrderId, orders[s.OrderId].Status, orders[s.OrderId].CodAmount),
                StringComparer.Ordinal);

        var now = clock.GetUtcNow();
        var payout = Payout.Record(command.Provider, reference, command.PaidOn, command.NetAmount, command.Note, command.Lines, parcels, request.UserId, now);

        foreach (var orderId in payout.Lines.Select(l => l.SettlesOrderId).OfType<Guid>())
        {
            orders[orderId].TransitionTo(OrderStatus.CodSettled, request.UserId, now, $"COD settled by {payout.Provider} payout {payout.Reference}");
        }

        db.Payouts.Add(payout);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException e) when (IsUniqueViolation(e, ReferenceIndex))
        {
            throw Duplicate(command.Provider, reference); // the same statement, entered twice at the same moment
        }
        catch (DbUpdateException e) when (e is DbUpdateConcurrencyException || IsUniqueViolation(e, SettledShipmentIndex))
        {
            // Another payout (or a status change) touched one of these orders since we read it.
            throw new ConflictException("An order on this payout was changed just now. Reload and record the payout again.");
        }

        var settled = payout.Lines.Count(l => l.SettlesOrderId is not null);
        LogRecorded(payout.Id, payout.Provider, settled, payout.Lines.Count - settled);
        return await GetAsync(payout.Id, cancellationToken);
    }

    public async Task<PayoutDetails> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var payout = await db.Payouts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("Payout", id);

        var orderIds = payout.Lines.Select(l => l.OrderId).OfType<Guid>().Distinct().ToList();
        var numbers = await db.Orders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.Number })
            .ToDictionaryAsync(o => o.Id, o => o.Number, cancellationToken);

        return new PayoutDetails(payout, numbers);
    }

    public async Task<PayoutPage> ListAsync(CourierProvider? provider, int page, int pageSize, CancellationToken cancellationToken)
    {
        // A shop gets a few payouts a week, so OFFSET paging stays cheap here (unlike the order list).
        var query = db.Payouts.AsNoTracking().Where(p => provider == null || p.Provider == provider);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.RecordedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PayoutPage(items, page, pageSize, total);
    }

    private static ConflictException Duplicate(CourierProvider provider, string reference) =>
        new($"The {provider} payout \"{reference}\" is already recorded.");

    private static bool IsUniqueViolation(DbUpdateException e, string index) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg && pg.ConstraintName == index;

    [LoggerMessage(Level = LogLevel.Information, Message = "Payout {PayoutId} from {Provider} recorded: {Settled} parcels settled, {Mismatched} mismatched")]
    private partial void LogRecorded(Guid payoutId, CourierProvider provider, int settled, int mismatched);
}
