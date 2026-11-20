namespace Hishab.Application.Orders;

/// <summary>A customer's track record with this shop: the numbers behind "is this buyer risky?".</summary>
public interface ICustomerOrderHistory
{
    /// <param name="excludingOrderId">Leave out this order (the one being shown), so it does not count itself.</param>
    Task<CustomerOrderHistory> ForAsync(Guid customerId, Guid? excludingOrderId, CancellationToken cancellationToken);

    /// <summary>What the customer has paid for: the totals of their delivered (and COD settled) orders, in BDT.</summary>
    Task<decimal> DeliveredValueAsync(Guid customerId, CancellationToken cancellationToken);
}

/// <summary>Delivered includes COD settled; returned includes received back.</summary>
public sealed record CustomerOrderHistory(int PreviousOrders, int Delivered, int Returned, int Cancelled);
