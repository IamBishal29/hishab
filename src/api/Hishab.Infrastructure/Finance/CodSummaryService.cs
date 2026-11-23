using Hishab.Application.Finance;
using Hishab.Domain.Couriers;
using Hishab.Domain.Finance;
using Hishab.Domain.Orders;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hishab.Infrastructure.Finance;

/// <summary>
/// Loads the shop's delivered and settled orders (tenant filter applies) and their couriers, then
/// lets the domain do the arithmetic. The COD amount is the domain rule <see cref="Order.CodAmount"/>
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

        var parcels = orders.Select(o => new CodParcel(
            couriers.TryGetValue(o.Id, out var provider) ? provider : null,
            o.CodAmount,
            DeliveredAt(o),
            Settled: o.Status == OrderStatus.CodSettled));

        return CodReconciliation.Summarize(parcels, clock.GetUtcNow());
    }

    /// <summary>When the order became Delivered (its latest such step); never missing for a delivered order.</summary>
    private static DateTimeOffset DeliveredAt(Order order) =>
        order.History.LastOrDefault(h => h.To == OrderStatus.Delivered)?.ChangedAt ?? order.CreatedAt;
}
