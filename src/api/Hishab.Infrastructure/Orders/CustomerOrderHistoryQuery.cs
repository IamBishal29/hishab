using Hishab.Application.Orders;
using Hishab.Domain.Orders;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hishab.Infrastructure.Orders;

/// <summary>One grouped count per status (served by the orders customer_id index), shared by order details and the quick-order lookup.</summary>
internal sealed class CustomerOrderHistoryQuery(HishabDbContext db) : ICustomerOrderHistory
{
    public async Task<CustomerOrderHistory> ForAsync(Guid customerId, Guid? excludingOrderId, CancellationToken cancellationToken)
    {
        var byStatus = await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.Id != excludingOrderId)
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

        // Recency for the risk rules: the latest Delivered and Returned steps in these orders' history.
        var latest = await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId && o.Id != excludingOrderId)
            .SelectMany(o => o.History)
            .Where(h => h.To == OrderStatus.Delivered || h.To == OrderStatus.Returned)
            .GroupBy(h => h.To)
            .Select(g => new { Step = g.Key, At = g.Max(h => h.ChangedAt) })
            .ToDictionaryAsync(x => x.Step, x => x.At, cancellationToken);

        return Build(byStatus, latest);
    }

    public async Task<IReadOnlyDictionary<Guid, CustomerOrderHistory>> ForManyAsync(
        IReadOnlyCollection<Guid> customerIds, CancellationToken cancellationToken)
    {
        if (customerIds.Count == 0)
        {
            return new Dictionary<Guid, CustomerOrderHistory>();
        }

        var ids = customerIds.Distinct().ToList();
        var counts = await db.Orders.AsNoTracking()
            .Where(o => ids.Contains(o.CustomerId))
            .GroupBy(o => new { o.CustomerId, o.Status })
            .Select(g => new { g.Key.CustomerId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var latest = await db.Orders.AsNoTracking()
            .Where(o => ids.Contains(o.CustomerId))
            .SelectMany(o => o.History, (o, h) => new { o.CustomerId, h.To, h.ChangedAt })
            .Where(x => x.To == OrderStatus.Delivered || x.To == OrderStatus.Returned)
            .GroupBy(x => new { x.CustomerId, x.To })
            .Select(g => new { g.Key.CustomerId, g.Key.To, At = g.Max(x => x.ChangedAt) })
            .ToListAsync(cancellationToken);

        return ids.ToDictionary(
            id => id,
            id => Build(
                counts.Where(c => c.CustomerId == id).ToDictionary(c => c.Status, c => c.Count),
                latest.Where(l => l.CustomerId == id).ToDictionary(l => l.To, l => l.At)));
    }

    /// <summary>One rule for both shapes: which statuses count as delivered, returned and cancelled.</summary>
    private static CustomerOrderHistory Build(Dictionary<OrderStatus, int> byStatus, Dictionary<OrderStatus, DateTimeOffset> latest)
    {
        int Count(params OrderStatus[] statuses) => statuses.Sum(s => byStatus.GetValueOrDefault(s));
        DateTimeOffset? At(OrderStatus step) => latest.TryGetValue(step, out var at) ? at : null;

        return new CustomerOrderHistory(
            PreviousOrders: byStatus.Values.Sum(),
            Delivered: Count(OrderStatus.Delivered, OrderStatus.CodSettled),
            Returned: Count(OrderStatus.Returned, OrderStatus.ReceivedBack),
            Cancelled: Count(OrderStatus.Cancelled),
            LastDeliveredAt: At(OrderStatus.Delivered),
            LastReturnedAt: At(OrderStatus.Returned));
    }

    // Order.Total is a domain rule (lines + delivery charge), not a column, so the delivered orders
    // are loaded and summed with that rule rather than re-implemented in SQL. One customer's
    // delivered orders are tens of rows, served by the (tenant_id, customer_id, created_at, number) index.
    public async Task<decimal> DeliveredValueAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var delivered = await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId
                && (o.Status == OrderStatus.Delivered || o.Status == OrderStatus.CodSettled))
            .ToListAsync(cancellationToken);
        return delivered.Sum(o => o.Total.Amount);
    }
}
