using Hishab.Application.Finance;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Finance;
using Hishab.Domain.Orders;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hishab.Infrastructure.Finance;

/// <summary>
/// Loads the shop's delivered and settled orders (tenant filter applies), their couriers and what
/// payouts paid for them, then lets the domain do the arithmetic. The COD amount is the domain rule <see cref="Order.CodAmount"/>
/// (lines + delivery charge - advance), not a column, so it is computed on the loaded orders rather
/// than re-implemented in SQL where the two copies could drift apart.
/// </summary>
internal sealed class CodSummaryService(HishabDbContext db, TimeProvider clock) : ICodSummaryService
{
    private static readonly OrderStatus[] _delivered = [OrderStatus.Delivered, OrderStatus.CodSettled];

    public async Task<CodSummary> GetAsync(CancellationToken cancellationToken)
    {
        // Lines and history are owned collections: split queries avoid a lines x history row explosion.
        var orders = await db.Orders.AsNoTracking().AsSplitQuery()
            .Where(o => _delivered.Contains(o.Status))
            .ToListAsync(cancellationToken);

        // The booked shipment says which courier holds the cash (one live shipment per order).
        var couriers = await db.Shipments.AsNoTracking()
            .Where(s => s.State == ShipmentState.Booked
                && db.Orders.Any(o => o.Id == s.OrderId && _delivered.Contains(o.Status)))
            .Select(s => new { s.OrderId, s.Provider })
            .ToDictionaryAsync(s => s.OrderId, s => s.Provider, cancellationToken);

        // What payouts actually paid per settled order (one settling line per parcel, by unique index).
        var paid = await db.Payouts.AsNoTracking()
            .SelectMany(p => p.Lines)
            .Where(l => l.SettledShipmentId != null)
            .Select(l => new { OrderId = l.OrderId!.Value, l.Collected })
            .ToDictionaryAsync(l => l.OrderId, l => l.Collected, cancellationToken);

        var parcels = orders.Select(o => new CodParcel(
            couriers.TryGetValue(o.Id, out var provider) ? provider : null,
            o.CodAmount,
            DeliveredAt(o),
            Paid: PaidFor(o, paid)));

        return CodReconciliation.Summarize(parcels, clock.GetUtcNow());
    }

    /// <summary>
    /// The payout line's amount; null while unpaid. A CodSettled order without a line (settled before
    /// payouts were recorded) counts as paid in full, as it did then.
    /// </summary>
    private static Money? PaidFor(Order order, Dictionary<Guid, Money> paid) =>
        paid.TryGetValue(order.Id, out var amount) ? amount
        : order.Status == OrderStatus.CodSettled ? order.CodAmount
        : null;

    /// <summary>When the order became Delivered (its latest such step); never missing for a delivered order.</summary>
    private static DateTimeOffset DeliveredAt(Order order) =>
        order.History.LastOrDefault(h => h.To == OrderStatus.Delivered)?.ChangedAt ?? order.CreatedAt;
}
