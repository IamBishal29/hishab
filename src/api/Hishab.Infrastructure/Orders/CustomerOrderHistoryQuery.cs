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

        int Count(params OrderStatus[] statuses) => statuses.Sum(s => byStatus.GetValueOrDefault(s));

        return new CustomerOrderHistory(
            PreviousOrders: byStatus.Values.Sum(),
            Delivered: Count(OrderStatus.Delivered, OrderStatus.CodSettled),
            Returned: Count(OrderStatus.Returned, OrderStatus.ReceivedBack),
            Cancelled: Count(OrderStatus.Cancelled));
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
