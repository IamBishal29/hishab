using System.Globalization;
using FluentValidation;
using Hishab.Api.Auth;
using Hishab.Application.Export;
using Hishab.Application.Orders;
using Hishab.Domain.Common;
using Hishab.Domain.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hishab.Api.Controllers;

/// <summary>The order list's filters, without paging: the file holds every matching order.</summary>
public sealed class OrderExportQuery
{
    /// <summary>One or more statuses; repeat the parameter for several.</summary>
    public IReadOnlyList<OrderStatus>? Status { get; init; }

    /// <summary>First day (Asia/Dhaka), inclusive. <example>2026-11-01</example></summary>
    public DateOnly? From { get; init; }

    /// <summary>Last day (Asia/Dhaka), inclusive. <example>2026-11-30</example></summary>
    public DateOnly? To { get; init; }

    public bool? IsInsideDhaka { get; init; }

    public OrderSource? Source { get; init; }

    /// <summary>Order number, part of the customer's name, or 3+ digits of their phone.</summary>
    public string? Search { get; init; }

    public Guid? CustomerId { get; init; }

    internal OrderListFilter ToFilter() => new(Status, From, To, IsInsideDhaka, Source, Search, CustomerId);
}

public sealed class OrderExportQueryValidator : AbstractValidator<OrderExportQuery>
{
    public OrderExportQueryValidator()
    {
        RuleForEach(q => q.Status).IsInEnum();
        RuleFor(q => q.Source).IsInEnum();
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From).When(q => q.From is not null && q.To is not null)
            .WithMessage("The last day must not be before the first day.");
        RuleFor(q => q.Search).MaximumLength(100);
    }
}

/// <summary>The customer list's search, without paging.</summary>
public sealed class CustomerExportQuery
{
    /// <summary>Part of a name, or at least 3 digits of a phone number.</summary>
    public string? Search { get; init; }
}

public sealed class CustomerExportQueryValidator : AbstractValidator<CustomerExportQuery>
{
    public CustomerExportQueryValidator() => RuleFor(q => q.Search).MaximumLength(100);
}

/// <summary>
/// The shop's data as CSV (UTF-8 with BOM, opens in Excel with Bangla intact). Owner only: the files
/// hold every customer's phone and address, and order money including cost.
/// </summary>
[ApiController]
[Route("export")]
[Authorize(Policy = Policies.CanSeeProfit)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class ExportController(IExportService exports, TimeProvider clock) : ControllerBase
{
    private const string CsvType = "text/csv; charset=utf-8";

    /// <summary>
    /// Every order matching the order list's filters, newest first, one row per order (items as text).
    /// Streamed: large shops download without the server loading everything first.
    /// </summary>
    [HttpGet("orders.csv")]
    [ProducesResponseType<Stream>(StatusCodes.Status200OK, "text/csv")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task Orders([FromQuery] OrderExportQuery query, CancellationToken cancellationToken)
    {
        StartFile("orders");
        await exports.WriteOrdersAsync(query.ToFilter(), Response.Body, cancellationToken);
    }

    /// <summary>Every customer matching the customer list's search, newest first, with order counts.</summary>
    [HttpGet("customers.csv")]
    [ProducesResponseType<Stream>(StatusCodes.Status200OK, "text/csv")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task Customers([FromQuery] CustomerExportQuery query, CancellationToken cancellationToken)
    {
        StartFile("customers");
        await exports.WriteCustomersAsync(query.Search, Response.Body, cancellationToken);
    }

    private void StartFile(string name)
    {
        var day = DhakaTime.DayOf(clock.GetUtcNow()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Response.ContentType = CsvType;
        Response.Headers.ContentDisposition = $"attachment; filename=\"{name}-{day}.csv\"";
        Response.Headers.CacheControl = "no-store"; // personal data: never kept by a proxy or the browser cache
    }
}
