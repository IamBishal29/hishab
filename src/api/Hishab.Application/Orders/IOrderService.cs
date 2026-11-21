using Hishab.Domain.Couriers;
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

    /// <summary>
    /// The shop's orders, newest first, filtered and keyset-paged: pass the previous page's
    /// <see cref="OrderListPage.NextCursor"/> to get the next one.
    /// </summary>
    /// <exception cref="Domain.Common.DomainValidationException">The cursor is not one this API issued.</exception>
    Task<OrderListPage> ListAsync(OrderListFilter filter, string? cursor, int limit, CancellationToken cancellationToken);

    /// <summary>Moves one order to <paramref name="target"/> (e.g. Confirmed, Cancelled) and records it in the history.</summary>
    /// <exception cref="Domain.Common.ConflictException">The lifecycle forbids the move, or someone else changed the order first.</exception>
    Task<OrderDetails> TransitionAsync(Guid id, OrderStatus target, string? note, CancellationToken cancellationToken);

    /// <summary>
    /// Moves each order independently: one order failing (wrong status, changed by someone else,
    /// not found) never stops or undoes the others. Every ID gets a result.
    /// </summary>
    Task<BulkActionResult> TransitionManyAsync(IReadOnlyList<Guid> ids, OrderStatus target, string? note, CancellationToken cancellationToken);
}

/// <summary>All filters are optional and combine with AND. Dates are Asia/Dhaka calendar days, both inclusive.</summary>
public sealed record OrderListFilter(
    IReadOnlyList<OrderStatus>? Statuses = null,
    DateOnly? From = null,
    DateOnly? To = null,
    bool? IsInsideDhaka = null,
    OrderSource? Source = null,
    string? Search = null,
    Guid? CustomerId = null);

public sealed record OrderListPage(IReadOnlyList<OrderListItem> Items, string? NextCursor);

/// <summary>One row of the order list: enough to scan and act on without opening the order.</summary>
public sealed record OrderListItem(
    Guid Id,
    int Number,
    OrderStatus Status,
    OrderSource Source,
    bool IsInsideDhaka,
    Guid CustomerId,
    string CustomerPhone,
    string? CustomerName,
    int ItemCount,
    decimal Total,
    decimal CodAmount,
    DateTimeOffset CreatedAt);

public sealed record BulkActionResult(int Succeeded, int Failed, IReadOnlyList<BulkItemResult> Results);

/// <summary>
/// The outcome for one order. <see cref="Error"/> is a stable code for the UI
/// (<see cref="BulkErrors"/>); <see cref="Message"/> is the human-readable reason.
/// </summary>
public sealed record BulkItemResult(Guid OrderId, bool Succeeded, OrderStatus? Status, string? Error, string? Message);

public static class BulkErrors
{
    public const string NotFound = "notFound";
    public const string InvalidTransition = "invalidTransition";
    public const string ConcurrentChange = "concurrentChange";

    /// <summary>The order cannot be sent yet (e.g. no delivery address).</summary>
    public const string Invalid = "invalid";

    /// <summary>The courier refused the parcel; fix the order and book again.</summary>
    public const string CourierRejected = "courierRejected";

    /// <summary>The courier did not answer; booking again is safe.</summary>
    public const string CourierUnavailable = "courierUnavailable";
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
/// <param name="Shipment">The latest courier booking for this order, if any.</param>
public sealed record OrderDetails(OrderDto Order, CustomerSummary Customer, IReadOnlyList<string> Warnings, ShipmentDto? Shipment);

public sealed record ShipmentDto(
    Guid Id,
    CourierProvider Provider,
    ShipmentState State,
    string? ConsignmentId,
    string? TrackingCode,
    decimal? DeliveryFee,
    string? Error,
    DateTimeOffset RequestedAt,
    DateTimeOffset? BookedAt);

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
