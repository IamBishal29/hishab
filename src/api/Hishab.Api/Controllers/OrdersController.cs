using FluentValidation;
using Hishab.Api.Auth;
using Hishab.Application.Couriers;
using Hishab.Application.Orders;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
    /// <summary>One or more statuses; repeat the parameter for several (status=New, then status=Confirmed).</summary>
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

    /// <summary>Only this customer's orders (the customer page).</summary>
    public Guid? CustomerId { get; init; }

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

public sealed class BookOrderRequest
{
    /// <summary>The courier to book with; omit to use the shop's default courier.</summary>
    /// <example>Fake</example>
    public CourierProvider? Courier { get; init; }

    /// <summary>
    /// Where the courier delivers, in its own location IDs (couriers with <c>supportsLocations</c>,
    /// from GET /api/couriers/{provider}/locations). Omit to book by address.
    /// </summary>
    public DestinationRequest? Destination { get; init; }
}

/// <summary>A courier's own location IDs: city and zone required, area optional.</summary>
public sealed class DestinationRequest
{
    /// <example>1</example>
    public required string CityId { get; init; }

    /// <example>11</example>
    public required string ZoneId { get; init; }

    /// <example>111</example>
    public string? AreaId { get; init; }

    /// <summary>The domain value object re-checks the IDs (400 keyed <c>destination.*</c>).</summary>
    public CourierDestination ToDomain() => new(CityId, ZoneId, AreaId);
}

public sealed class BookOrderRequestValidator : AbstractValidator<BookOrderRequest>
{
    public BookOrderRequestValidator()
    {
        RuleFor(r => r.Courier).IsInEnum();
        When(r => r.Destination is not null, () =>
        {
            RuleFor(r => r.Destination!.CityId).NotEmpty().MaximumLength(CourierDestination.IdMaxLength);
            RuleFor(r => r.Destination!.ZoneId).NotEmpty().MaximumLength(CourierDestination.IdMaxLength);
            RuleFor(r => r.Destination!.AreaId).MaximumLength(CourierDestination.IdMaxLength);
        });
    }
}

/// <summary>A courier's price for one order.</summary>
public sealed record CourierQuoteResponse(decimal DeliveryFee, decimal CodCharge);

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

public sealed class LabelsQuery
{
    /// <summary>Booked orders to print, at most 100; repeat the parameter: <c>?ids=..&amp;ids=..</c>.</summary>
    public required IReadOnlyList<Guid> Ids { get; init; }

    /// <summary>A4 sheet (8 per page) or thermal 100 × 150 mm (one per page).</summary>
    /// <example>Thermal</example>
    public LabelFormat Format { get; init; } = LabelFormat.A4;
}

public sealed class LabelsQueryValidator : AbstractValidator<LabelsQuery>
{
    public LabelsQueryValidator()
    {
        RuleFor(q => q.Ids).NotEmpty()
            .Must(ids => ids.Count <= ILabelService.MaxOrders).WithMessage($"At most {ILabelService.MaxOrders} labels at a time.");
        RuleForEach(q => q.Ids).NotEmpty();
        RuleFor(q => q.Format).IsInEnum();
    }
}

public sealed class InvoicesQuery
{
    /// <summary>Orders to print (any status but Cancelled), at most 100; repeat the parameter: <c>?ids=..&amp;ids=..</c>.</summary>
    public required IReadOnlyList<Guid> Ids { get; init; }

    /// <summary>A5 (folds into the parcel, default) or A4; one invoice per page.</summary>
    /// <example>A5</example>
    public InvoiceFormat Format { get; init; } = InvoiceFormat.A5;
}

public sealed class InvoicesQueryValidator : AbstractValidator<InvoicesQuery>
{
    public InvoicesQueryValidator()
    {
        RuleFor(q => q.Ids).NotEmpty()
            .Must(ids => ids.Count <= IInvoiceService.MaxOrders).WithMessage($"At most {IInvoiceService.MaxOrders} invoices at a time.");
        RuleForEach(q => q.Ids).NotEmpty();
        RuleFor(q => q.Format).IsInEnum();
    }
}

public sealed class BulkBookRequest
{
    /// <summary>Confirmed orders to book (at most Booking:MaxBatchSize, 100 by default); each succeeds or fails on its own.</summary>
    public required IReadOnlyList<Guid> OrderIds { get; init; }

    /// <summary>The courier to book with; omit to use the shop's default courier.</summary>
    /// <example>Fake</example>
    public CourierProvider? Courier { get; init; }
}

public sealed class BulkBookRequestValidator : AbstractValidator<BulkBookRequest>
{
    public BulkBookRequestValidator(IOptions<BookingOptions> options)
    {
        var max = options.Value.MaxBatchSize;
        RuleFor(r => r.OrderIds).NotEmpty()
            .Must(ids => ids.Count <= max).WithMessage($"At most {max} orders at a time.");
        RuleForEach(r => r.OrderIds).NotEmpty();
        RuleFor(r => r.Courier).IsInEnum();
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
public sealed class OrdersController(
    IOrderService orders, IShipmentService shipments, ILabelService labels, IInvoiceService invoices) : ControllerBase
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
            new OrderListFilter(query.Status, query.From, query.To, query.IsInsideDhaka, query.Source, query.Search, query.CustomerId),
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

    /// <summary>
    /// Books a Confirmed order with a courier (the shop's default when none is given) and moves it
    /// to Booked. Safe to retry: an already booked order returns its booking, and the courier
    /// always receives the same reference, so it never books the parcel twice. 409 if the order is
    /// not Confirmed; 400 if no courier is connected or the courier refuses the parcel; 503 if the
    /// courier did not answer (retry).
    /// </summary>
    [HttpPost("{id:guid}/book")]
    [Authorize(Policy = Policies.CanPack)]
    [ProducesResponseType<OrderDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrderDetails>> Book(Guid id, BookOrderRequest? request, CancellationToken cancellationToken) =>
        Ok(await shipments.BookAsync(id, request?.Courier, request?.Destination?.ToDomain(), cancellationToken));

    /// <summary>
    /// The courier's price for this order (delivery fee and COD charge), to a destination for
    /// couriers that price by location (Pathao needs city and zone). Saves nothing. 400 if a
    /// destination is needed or not in the courier's lists; 503 if the courier did not answer.
    /// </summary>
    [HttpPost("{id:guid}/quote")]
    [Authorize(Policy = Policies.CanPack)]
    [ProducesResponseType<CourierQuoteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CourierQuoteResponse>> Quote(Guid id, BookOrderRequest? request, CancellationToken cancellationToken)
    {
        var quote = await shipments.QuoteAsync(id, request?.Courier, request?.Destination?.ToDomain(), cancellationToken);
        return Ok(new CourierQuoteResponse(quote.DeliveryFee.Amount, quote.CodCharge.Amount));
    }

    /// <summary>
    /// Shipping labels for booked orders as one PDF (Bangla supported), in the order given.
    /// 400 if an order is not booked yet; 404 if an order is not in this shop; 503 if printing is
    /// unavailable right now.
    /// </summary>
    [HttpGet("labels")]
    [Authorize(Policy = Policies.CanPack)]
    [ProducesResponseType<Stream>(StatusCodes.Status200OK, "application/pdf")] // binary in the OpenAPI document
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Labels([FromQuery] LabelsQuery query, CancellationToken cancellationToken)
    {
        var document = await labels.RenderAsync(query.Ids, query.Format, cancellationToken);
        return File(document.Pdf, "application/pdf", $"labels-{query.Format.ToString().ToLowerInvariant()}.pdf");
    }

    /// <summary>
    /// Invoices for the buyer as one PDF, one per page (Bangla supported), in the order given.
    /// Selling prices only, never costs. 400 if an order is cancelled; 404 if an order is not in
    /// this shop; 503 if printing is unavailable right now.
    /// </summary>
    [HttpGet("invoices")]
    [Authorize(Policy = Policies.CanPack)]
    [ProducesResponseType<Stream>(StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Invoices([FromQuery] InvoicesQuery query, CancellationToken cancellationToken)
    {
        var document = await invoices.RenderAsync(query.Ids, query.Format, cancellationToken);
        return File(document.Pdf, "application/pdf", $"invoices-{query.Format.ToString().ToLowerInvariant()}.pdf");
    }

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

    /// <summary>
    /// Books many Confirmed orders with one courier (the shop's default when none is given). Always
    /// 200 with one result per order: booked orders stay booked when others fail. Failed orders can
    /// be sent again safely (<c>courierUnavailable</c> never creates a second parcel). 400 only when
    /// no courier can be used at all.
    /// </summary>
    [HttpPost("bulk/book")]
    [Authorize(Policy = Policies.CanPack)]
    [ProducesResponseType<BulkActionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BulkActionResult>> BulkBook(BulkBookRequest request, CancellationToken cancellationToken) =>
        Ok(await shipments.BookManyAsync(request.OrderIds, request.Courier, cancellationToken));

    /// <summary>Cancels many orders, each on its own (see bulk confirm).</summary>
    [HttpPost("bulk/cancel")]
    [Authorize(Policy = Policies.CanEnterOrders)]
    [ProducesResponseType<BulkActionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BulkActionResult>> BulkCancel(BulkOrderActionRequest request, CancellationToken cancellationToken) =>
        Ok(await orders.TransitionManyAsync(request.OrderIds, OrderStatus.Cancelled, request.Note, cancellationToken));
}
