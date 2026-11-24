using FluentValidation;
using Hishab.Api.Auth;
using Hishab.Application.Customers;
using Hishab.Application.Orders;
using Hishab.Domain.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hishab.Api.Controllers;

/// <summary>The phone travels in the body, never the URL: URLs end up in proxy logs, browser history and Referer headers.</summary>
public sealed class CustomerLookupRequest
{
    /// <example>01712345678</example>
    public required string Phone { get; init; }
}

public sealed class FindOrCreateCustomerRequest
{
    /// <example>01712345678</example>
    public required string Phone { get; init; }

    /// <example>Rahima Begum</example>
    public string? Name { get; init; }

    /// <example>House 4, Road 2, Mirpur 10, Dhaka</example>
    public string? Address { get; init; }
}

public sealed class UpdateCustomerRequest
{
    /// <example>Rahima Begum</example>
    public string? Name { get; init; }

    /// <example>House 4, Road 2, Mirpur 10, Dhaka</example>
    public string? Address { get; init; }

    /// <example>Prefers delivery after 5 pm.</example>
    public string? Notes { get; init; }
}

public sealed class BlockCustomerRequest
{
    /// <example>Refused 3 parcels in a row.</example>
    public string? Reason { get; init; }
}

public sealed class CustomerListQuery
{
    /// <summary>Part of a name, or at least 3 digits of a phone number.</summary>
    /// <example>0171</example>
    public string? Search { get; init; }

    /// <example>1</example>
    public int Page { get; init; } = 1;

    /// <example>20</example>
    public int PageSize { get; init; } = 20;
}

public sealed class CustomerLookupRequestValidator : AbstractValidator<CustomerLookupRequest>
{
    public CustomerLookupRequestValidator() => RuleFor(r => r.Phone).NotEmpty().MaximumLength(20);
}

public sealed class FindOrCreateCustomerRequestValidator : AbstractValidator<FindOrCreateCustomerRequest>
{
    public FindOrCreateCustomerRequestValidator()
    {
        RuleFor(r => r.Phone).NotEmpty().MaximumLength(20);
        RuleFor(r => r.Name).MaximumLength(Customer.NameMaxLength);
        RuleFor(r => r.Address).MaximumLength(Customer.AddressMaxLength);
    }
}

public sealed class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(r => r.Name).MaximumLength(Customer.NameMaxLength);
        RuleFor(r => r.Address).MaximumLength(Customer.AddressMaxLength);
        RuleFor(r => r.Notes).MaximumLength(Customer.NotesMaxLength);
    }
}

public sealed class BlockCustomerRequestValidator : AbstractValidator<BlockCustomerRequest>
{
    public BlockCustomerRequestValidator() => RuleFor(r => r.Reason).MaximumLength(Customer.BlockReasonMaxLength);
}

public sealed class CustomerListQueryValidator : AbstractValidator<CustomerListQuery>
{
    public CustomerListQueryValidator()
    {
        RuleFor(q => q.Search).MaximumLength(100);
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}

/// <summary>The customer, their track record with this shop and the risk badge it gives.</summary>
/// <param name="Customer">The customer's details.</param>
/// <param name="History">Order counts by outcome in this shop.</param>
/// <param name="Risk">New, Safe, Watch or Risky, with every reason that applies (this shop's history only).</param>
public sealed record CustomerLookupResponse(CustomerDto Customer, CustomerOrderHistory History, RiskAssessment Risk);

/// <summary>The customer page: details, track record and what they have paid for.</summary>
/// <param name="Customer">The customer's details.</param>
/// <param name="History">Order counts by outcome in this shop.</param>
/// <param name="DeliveredValue">Sum of the totals of delivered and COD-settled orders, in BDT.</param>
/// <param name="Risk">New, Safe, Watch or Risky, with every reason that applies (this shop's history only).</param>
public sealed record CustomerDetails(CustomerDto Customer, CustomerOrderHistory History, decimal DeliveredValue, RiskAssessment Risk);

/// <summary>Customers of this shop. Staff who enter orders (Owner, Manager, Moderator).</summary>
[ApiController]
[Route("customers")]
[Authorize(Policy = Policies.CanEnterOrders)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class CustomersController(ICustomerService customers, ICustomerOrderHistory history, ICustomerRiskAssessor risk) : ControllerBase
{
    /// <summary>Lists customers, newest first; search by name or phone digits.</summary>
    [HttpGet]
    [ProducesResponseType<CustomerPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CustomerPage>> List([FromQuery] CustomerListQuery query, CancellationToken cancellationToken) =>
        Ok(await customers.ListAsync(query.Search, query.Page, query.PageSize, cancellationToken));

    /// <summary>
    /// Quick-order auto-fill: finds the customer for a phone (any spelling), with their order
    /// history in this shop. 404 means a new customer. Check isBlocked before taking the order.
    /// </summary>
    [HttpPost("lookup")]
    [ProducesResponseType<CustomerLookupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerLookupResponse>> Lookup(CustomerLookupRequest request, CancellationToken cancellationToken)
    {
        if (await customers.FindByPhoneAsync(request.Phone, cancellationToken) is not { } customer)
        {
            return NotFound();
        }

        var record = await history.ForAsync(customer.Id, null, cancellationToken);
        return Ok(new CustomerLookupResponse(customer, record, risk.Assess(record, customer.IsBlocked)));
    }

    /// <summary>Returns the customer for this phone, creating it when new (201) or matching it (200; only missing details are filled).</summary>
    [HttpPost]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CustomerDto>> FindOrCreate(FindOrCreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var (customer, created) = await customers.FindOrCreateAsync(request.Phone, request.Name, request.Address, cancellationToken);
        return created ? Created($"/api/customers/{customer.Id}", customer) : Ok(customer);
    }

    /// <summary>The customer with their order history in this shop (counts, delivered value and risk badge).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CustomerDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDetails>> Get(Guid id, CancellationToken cancellationToken)
    {
        var customer = await customers.GetAsync(id, cancellationToken); // 404 (also for another shop's customer)
        var record = await history.ForAsync(id, null, cancellationToken);
        return Ok(new CustomerDetails(
            customer,
            record,
            await history.DeliveredValueAsync(id, cancellationToken),
            risk.Assess(record, customer.IsBlocked)));
    }

    /// <summary>Replaces name, address and notes. The phone (the customer's identity) cannot change.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDto>> Update(Guid id, UpdateCustomerRequest request, CancellationToken cancellationToken) =>
        Ok(await customers.UpdateAsync(id, request.Name, request.Address, request.Notes, cancellationToken));

    /// <summary>Blocks the customer; order entry shows a warning for blocked customers.</summary>
    [HttpPost("{id:guid}/block")]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDto>> Block(Guid id, BlockCustomerRequest request, CancellationToken cancellationToken) =>
        Ok(await customers.BlockAsync(id, request.Reason, cancellationToken));

    [HttpDelete("{id:guid}/block")]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDto>> Unblock(Guid id, CancellationToken cancellationToken) =>
        Ok(await customers.UnblockAsync(id, cancellationToken));
}
