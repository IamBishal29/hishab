using Hishab.Application.Common.Tenancy;
using Hishab.Application.Reports;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Orders;
using Hishab.Domain.Reports;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hishab.Infrastructure.Reports;

/// <summary>
/// Reports computed on read: a fixed number of queries per request (no N+1), each an index range on
/// this shop's rows. Status history carries a tenant copy (index tenant_id, to, changed_at), so
/// "delivered between two instants" never scans other shops. Pre-aggregation can come later if a
/// month of one shop's parcels ever gets too big to load.
/// </summary>
internal sealed class DashboardService(HishabDbContext db, ITenantContext tenant, TimeProvider clock) : IDashboardService
{
    private static readonly OrderStatus[] _finished = [OrderStatus.Delivered, OrderStatus.Returned];

    public async Task<TodayDashboard> TodayAsync(CancellationToken cancellationToken)
    {
        var today = DhakaTime.DayOf(clock.GetUtcNow());
        var (start, end) = (DhakaTime.StartOfDay(today), DhakaTime.StartOfDay(today.AddDays(1)));

        var newToday = await db.Orders.CountAsync(o => o.CreatedAt >= start && o.CreatedAt < end, cancellationToken);
        var byStatus = await db.Orders
            .Where(o => o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Booked || o.Status == OrderStatus.InTransit)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);

        var profit = await ProfitAsync(today, today, cancellationToken);
        var counts = new TodayCounts(
            newToday,
            byStatus.GetValueOrDefault(OrderStatus.Confirmed),
            byStatus.GetValueOrDefault(OrderStatus.Booked) + byStatus.GetValueOrDefault(OrderStatus.InTransit),
            profit.Total.Delivered,
            profit.Total.Returned);
        return new TodayDashboard(today, counts, profit);
    }

    public async Task<ProfitReport> ProfitAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken)
    {
        ProfitReport.Build(first, last, [], []); // validates the range before any query
        var (start, end) = (DhakaTime.StartOfDay(first), DhakaTime.StartOfDay(last.AddDays(1)));
        var tenantId = tenant.TenantId ?? throw new InvalidOperationException("Reports need a tenant.");

        // 1. Which parcels finished in the range, and when (the explicit tenant condition uses the index).
        var finished = await db.Orders
            .SelectMany(o => o.History, (o, h) => new { OrderId = o.Id, h })
            .Where(x => EF.Property<Guid>(x.h, HishabDbContext.OwnedTenantIdProperty) == tenantId
                && _finished.Contains(x.h.To) && x.h.ChangedAt >= start && x.h.ChangedAt < end)
            .Select(x => new { x.OrderId, x.h.To, x.h.ChangedAt })
            .ToListAsync(cancellationToken);
        if (finished.Count == 0)
        {
            return ProfitReport.Build(first, last, [], []);
        }

        // 2. Their orders (totals and line costs are domain calculations on the snapshot lines).
        var orderIds = finished.Select(f => f.OrderId).Distinct().ToList();
        var orders = await db.Orders.AsNoTracking().AsSplitQuery()
            .Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, cancellationToken);

        // 3. The parcels: booking quote and return charge.
        var shipments = await db.Shipments.AsNoTracking()
            .Where(s => orderIds.Contains(s.OrderId) && s.State == ShipmentState.Booked)
            .Select(s => new { s.Id, s.OrderId, s.DeliveryFee, s.ReturnCharge })
            .ToDictionaryAsync(s => s.OrderId, cancellationToken);

        // 4. What the courier actually deducted, for parcels a payout has settled.
        var shipmentIds = shipments.Values.Select(s => (Guid?)s.Id).ToList();
        var paid = await db.Payouts.AsNoTracking()
            .SelectMany(p => p.Lines)
            .Where(l => shipmentIds.Contains(l.SettledShipmentId))
            .Select(l => new { ShipmentId = l.SettledShipmentId!.Value, l.DeliveryFee, l.CodFee })
            .ToDictionaryAsync(l => l.ShipmentId, cancellationToken);

        var sales = new List<DeliveredSale>();
        var returns = new List<ReturnedParcel>();
        foreach (var f in finished)
        {
            var order = orders[f.OrderId];
            var shipment = shipments.GetValueOrDefault(f.OrderId);
            var day = DhakaTime.DayOf(f.ChangedAt);
            if (f.To == OrderStatus.Returned)
            {
                returns.Add(new ReturnedParcel(day, shipment?.ReturnCharge));
                continue;
            }

            var payout = shipment is null ? null : paid.GetValueOrDefault(shipment.Id);
            var cost = order.Lines.Where(l => l.UnitCost is not null)
                .Aggregate(Money.Zero, (sum, l) => sum + Money.Bdt(l.UnitCost!.Value.Amount * l.Quantity));
            sales.Add(new DeliveredSale(
                day,
                order.Total,
                cost,
                order.Lines.Count(l => l.UnitCost is null),
                payout?.DeliveryFee ?? shipment?.DeliveryFee,
                payout?.CodFee));
        }

        return ProfitReport.Build(first, last, sales, returns);
    }
}
