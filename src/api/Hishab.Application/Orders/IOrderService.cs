using Hishab.Domain.Orders;

namespace Hishab.Application.Orders;

/// <summary>Creates and reads the current shop's orders.</summary>
public interface IOrderService
{
    /// <summary>
    /// Creates an order in one transaction: matches or creates the customer by phone, snapshots
    /// the catalogue lines, applies the shop's delivery charge (unless overridden), counts the
    /// order against the plan and gives it the next number. The idempotency key makes retries
    /// safe: the same key and request return the first order; the same key with a different
    /// request is refused.
    /// </summary>
    /// <exception cref="Domain.Shops.PlanLimitExceededException">The monthly order limit is reached.</exception>
    /// <exception cref="Domain.Common.ConflictException">The key was used for a different request.</exception>
    Task<CreateOrderResult> CreateAsync(CreateOrderCommand command, string idempotencyKey, CancellationToken cancellationToken);

    Task<OrderDetails> GetAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record CreateOrderCommand(
    string Phone,
    string? CustomerName,
    string? DeliveryAddress,
    bool IsInsideDhaka,
    OrderSource Source,
    IReadOnlyList<OrderLineInput> Lines,
    decimal? DeliveryCharge,
    decimal AdvancePaid,
    string? Note);

public sealed record OrderLineInput(Guid VariantId, int Quantity);

public sealed record CreateOrderResult(OrderDetails Details, bool Replayed);

/// <summary>The order, the customer's track record with this shop, and warnings for the person taking the order.</summary>
public sealed record OrderDetails(OrderDto Order, CustomerSummary Customer, IReadOnlyList<string> Warnings);

public sealed record OrderDto(
    Guid Id,
    int Number,
    OrderStatus Status,
    OrderSource Source,
    string? DeliveryAddress,
    bool IsInsideDhaka,
    IReadOnlyList<OrderLineDto> Lines,
    decimal Subtotal,
    decimal DeliveryCharge,
    decimal Total,
    decimal AdvancePaid,
    decimal CodAmount,
    string? Note,
    DateTimeOffset CreatedAt,
    IReadOnlyList<OrderStatusChangeDto> History);

/// <summary><see cref="UnitCost"/> is null unless the caller may see profit.</summary>
public sealed record OrderLineDto(
    Guid? ProductVariantId,
    string ProductName,
    string? VariantLabel,
    string? Sku,
    decimal UnitPrice,
    decimal? UnitCost,
    int Quantity,
    decimal LineTotal);

public sealed record OrderStatusChangeDto(OrderStatus? From, OrderStatus To, Guid? ChangedBy, DateTimeOffset ChangedAt, string? Note);

/// <summary>Earlier orders of this customer with this shop (the current order excluded): feeds the risk check.</summary>
public sealed record CustomerSummary(
    Guid Id,
    string Phone,
    string? Name,
    bool IsBlocked,
    string? BlockedReason,
    int PreviousOrders,
    int Delivered,
    int Returned,
    int Cancelled);

public static class OrderWarnings
{
    /// <summary>The customer is on the shop's blocklist: take the order only after checking.</summary>
    public const string CustomerBlocked = "customerBlocked";
}
