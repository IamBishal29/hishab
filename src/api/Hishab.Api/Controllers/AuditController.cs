using FluentValidation;
using Hishab.Api.Auth;
using Hishab.Application.Common.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hishab.Api.Controllers;

public sealed class AuditQuery
{
    public const int MaxPageSize = 100;

    /// <summary>Starts at 1.</summary>
    /// <example>1</example>
    public int Page { get; init; } = 1;

    /// <summary>1 to 100.</summary>
    /// <example>20</example>
    public int PageSize { get; init; } = 20;
}

public sealed class AuditQueryValidator : AbstractValidator<AuditQuery>
{
    public AuditQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, AuditQuery.MaxPageSize);
    }
}

/// <summary>Who changed what in this shop. Owner only.</summary>
[ApiController]
[Route("audit")]
[Authorize(Policy = Policies.CanViewAudit)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AuditController(IAuditLog audit) : ControllerBase
{
    /// <summary>Lists audit entries, newest first, one page at a time.</summary>
    [HttpGet]
    [ProducesResponseType<AuditPage>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuditPage>> List([FromQuery] AuditQuery query, CancellationToken cancellationToken) =>
        Ok(await audit.ListAsync(query.Page, query.PageSize, cancellationToken));
}
