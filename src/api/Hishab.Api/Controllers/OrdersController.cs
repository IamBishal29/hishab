using FluentValidation;
using Hishab.Api.Auth;
using Hishab.Application.Orders;
using Hishab.Domain.Common;
using Hishab.Domain.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hishab.Api.Controllers;

public sealed class CreateOrderLineRequest
{
    /// <summary>The catalogue variant; name, SKU, price and cost are copied from it.</summary>
    public required Guid VariantId { get; init; }

    /// <example>2</example>
    public required int Quantity { get; init; }
}

public sealed class CreateOrderRequest
{
    /// <summary>The buyer's mobile number; matches an existing customer or creates one.</summary>
    /// <example>01912345678</example>
    public required string Phone { get; init; }

    /// <example>Rahima Begum</example>
    public string? CustomerName { get; init; }

    /// <summary>Defaults to the customer's saved address.</summary>
    /// <example>House 4, Road 2, Mirpur 10, Dhaka</example>
    public string? DeliveryAddress { get; init; }

    /// <summary>Picks the shop's inside or outside Dhaka delivery charge.</summary>
    /// <example>true</example>
    public required bool IsInsideDhaka { get; init; }

    /// <example>Facebook</example>
    public required OrderSource Source { get; init; }

    public required IReadOnlyList<CreateOrderLineRequest> Lines { get; init; }

    /// <summary>Overrides the shop's delivery charge (BDT), e.g. free delivery for a loyal buyer.</summary>
    /// <example>60</example>
    public decimal? DeliveryCharge { get; init; }

    /// <summary>Paid before delivery (bKash, Nagad), BDT.</summary>
    /// <example>0</example>
    public decimal AdvancePaid { get; init; }

    /// <example>Call before 5 pm</example>
    public string? Note { get; init; }

    internal CreateOrderCommand ToCommand() => new(
        Phone, CustomerName, DeliveryAddress, IsInsideDhaka, Source,
        [.. Lines.Select(l => new OrderLineInput(l.VariantId, l.Quantity))],
        DeliveryCharge, AdvancePaid, Note);
}

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(r => r.Phone).NotEmpty().MaximumLength(20);
        RuleFor(r => r.CustomerName).MaximumLength(Hishab.Domain.Customers.Customer.NameMaxLength);
        RuleFor(r => r.DeliveryAddress).MaximumLength(Order.AddressMaxLength);
        RuleFor(r => r.Source).IsInEnum();
        RuleFor(r => r.Note).MaximumLength(Order.NoteMaxLength);
        RuleFor(r => r.Lines).NotEmpty().WithMessage("Add at least one product.")
            .Must(l => l.Count <= Order.MaxLines).WithMessage($"An order can have at most {Order.MaxLines} lines.")
            .Must(l => l.Select(x => x.VariantId).Distinct().Count() == l.Count).WithMessage("Each product can appear once; change its quantity instead.");
        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.VariantId).NotEmpty();
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, OrderLine.MaxQuantity);
        });
        RuleFor(r => r.DeliveryCharge!.Value).InclusiveBetween(0m, 5_000m).Must(Money.HasValidScale)
            .OverridePropertyName(nameof(CreateOrderRequest.DeliveryCharge))
            .When(r => r.DeliveryCharge is not null);
        RuleFor(r => r.AdvancePaid).GreaterThanOrEqualTo(0m).Must(Money.HasValidScale).WithMessage("Advance can have at most 2 decimal places.");
    }
}

public sealed class OrderListQuery
{
    /// <summary>One or more statuses (repeat the parameter: <c>status=New&amp;status=Confirmed</c>).</summary>
    public IReadOnlyList<OrderStatus>? Status { get; init; }

    /// <summary>First day (Asia/Dhaka), inclusive.</summary>
    /// <example>2026-11-01</example>
    public DateOnly? From { get; init; }

    /// <summary>Last day (Asia/Dhaka), inclusive.</summary>
    /// <example>2026-11-30</example>
    public DateOnly? To { get; init; }

    /// <summary>Area: true for inside Dhaka, false for outside.</summary>
    public bool? IsInsideDhaka { get; init; }

    public OrderSource? Source { get; init; }

    /// <summary>Order number ("#1043"), part of the customer's name, or 3+ digits of their phone.</summary>
    /// <example>#1043</example>
    public string? Search { get; init; }

    /// <summary>The <c>nextCursor</c> of the previous page; omit for the first page.</summary>
    public string? Cursor { get; init; }

    /// <example>25</example>
    public int Limit { get; init; } = 25;
}

public sealed class OrderListQueryValidator : AbstractValidator<OrderListQuery>
{
    public OrderListQueryValidator()
    {
        RuleForEach(q => q.Status).IsInEnum();
        RuleFor(q => q.Source).IsInEnum();
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From).When(q => q.From is not null && q.To is not null)
            .WithMessage("The last day must not be before the first day.");
        RuleFor(q => q.Search).MaximumLength(100);
        RuleFor(q => q.Cursor).MaximumLength(64);
        RuleFor(q => q.Limit).InclusiveBetween(1, 100);
    }
}

public sealed class OrderActionRequest
{
    /// <summary>Why, e.g. a cancellation reason; saved in the order's history.</summary>
    /// <example>Customer asked to cancel on Messenger</example>
    public string? Note { get; init; }
}

public sealed class OrderActionRequestValidator : AbstractValidator<OrderActionRequest>
{
    public OrderActionRequestValidator() => RuleFor(r => r.Note).MaximumLength(Order.NoteMaxLength);
}

public sealed class BulkOrderActionRequest
{
    public const int MaxOrders = 100;

    /// <summary>Up to 100 order IDs; each succeeds or fails on its own.</summary>
    public required IReadOnlyList<Guid> OrderIds { get; init; }

    /// <example>Customer asked to cancel on Messenger</example>
    public string? Note { get; init; }
}

public sealed class BulkOrderActionRequestValidator : AbstractValidator<BulkOrderActionRequest>
{
    public BulkOrderActionRequestValidator()
    {
        RuleFor(r => r.OrderIds).NotEmpty()
            .Must(ids => ids.Count <= BulkOrderActionRequest.MaxOrders).WithMessage($"At most {BulkOrderActionRequest.MaxOrders} orders at a time.");
        RuleForEach(r => r.OrderIds).NotEmpty();
        RuleFor(r => r.Note).MaximumLength(Order.NoteMaxLength);
    }
}

/// <summary>
/// Orders of this shop. Every staff member can read them (packers work from the list);
/// creating, confirming and cancelling need <see cref="Policies.CanEnterOrders"/> (Owner, Manager, Moderator).
/// </summary>
[ApiController]
[Route("orders")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class OrdersController(IOrderService orders) : ControllerBase
{
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    /// <summary>
    /// Creates an order in one request. Send a new Idempotency-Key (e.g. a UUID) per order form
    /// submission: retrying with the same key returns the first order instead of creating another.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Policies.CanEnterOrders)]
    [ProducesResponseType<OrderDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDetails>> Create(
        CreateOrderRequest request,
        [FromHeader(Name = IdempotencyHeader)] string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey.Length > IdempotencyRecord.KeyMaxLength)
        {
            throw new DomainValidationException(IdempotencyHeader, $"The key is at most {IdempotencyRecord.KeyMaxLength} characters.");
        }

        var result = await orders.CreateAsync(request.ToCommand(), idempotencyKey, cancellationToken);
        if (result.Replayed)
        {
            Response.Headers[ReplayedHeader] = "true"; // same status and body as the first response
        }

        return Created($"/api/orders/{result.Details.Order.Id}", result.Details);
    }

    /// <summary>The order with its lines, history and the customer's track record.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<OrderDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetails>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(await orders.GetAsync(id, cancellationToken));

    /// <summary>
    /// The shop's orders, newest first. Filters combine with AND. Keyset paging: pass the
    /// returned <c>nextCursor</c> to get the next page; it is null on the last page.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<OrderListPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderListPage>> List([FromQuery] OrderListQuery query, CancellationToken cancellationToken) =>
        Ok(await orders.ListAsync(
            new OrderListFilter(query.Status, query.From, query.To, query.IsInsideDhaka, query.Source, query.Search),
            query.Cursor,
            query.Limit,
            cancellationToken));

    /// <summary>Confirms a New order (the buyer said yes on the phone). 409 if it is not New or changed meanwhile.</summary>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = Policies.CanEnterOrders)]
    [ProducesResponseType<OrderDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDetails>> Confirm(Guid id, OrderActionRequest? request, CancellationToken cancellationToken) =>
        Ok(await orders.TransitionAsync(id, OrderStatus.Confirmed, request?.Note, cancellationToken));

    /// <summary>Cancels a New or Confirmed order. 409 once it is booked with a courier or changed meanwhile.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.CanEnterOrders)]
    [ProducesResponseType<OrderDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderDetails>> Cancel(Guid id, OrderActionRequest? request, CancellationToken cancellationToken) =>
        Ok(await orders.TransitionAsync(id, OrderStatus.Cancelled, request?.Note, cancellationToken));

    /// <summary>
    /// Confirms many orders. Always 200 with one result per order: check <c>failed</c> and each
    /// result's <c>error</c>; successful orders stay confirmed even when others fail.
    /// </summary>
    [HttpPost("bulk/confirm")]
    [Authorize(Policy = Policies.CanEnterOrders)]
    [ProducesResponseType<BulkActionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BulkActionResult>> BulkConfirm(BulkOrderActionRequest request, CancellationToken cancellationToken) =>
        Ok(await orders.TransitionManyAsync(request.OrderIds, OrderStatus.Confirmed, request.Note, cancellationToken));

    /// <summary>Cancels many orders, each on its own (see bulk confirm).</summary>
    [HttpPost("bulk/cancel")]
    [Authorize(Policy = Policies.CanEnterOrders)]
    [ProducesResponseType<BulkActionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BulkActionResult>> BulkCancel(BulkOrderActionRequest request, CancellationToken cancellationToken) =>
        Ok(await orders.TransitionManyAsync(request.OrderIds, OrderStatus.Cancelled, request.Note, cancellationToken));
}
