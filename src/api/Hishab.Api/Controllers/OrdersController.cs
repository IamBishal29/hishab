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

/// <summary>Orders of this shop. Staff who enter orders (Owner, Manager, Moderator).</summary>
[ApiController]
[Route("orders")]
[Authorize(Policy = Policies.CanEnterOrders)]
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
}
