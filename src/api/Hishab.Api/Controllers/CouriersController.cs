using FluentValidation;
using Hishab.Api.Auth;
using Hishab.Application.Couriers;
using Hishab.Domain.Couriers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hishab.Api.Controllers;

public sealed class ConnectCourierRequest
{
    /// <summary>The courier's fields (see <c>fields</c> in GET /api/couriers), e.g. <c>{ "apiKey": "..." }</c>. Stored encrypted, never returned.</summary>
    public Dictionary<string, string> Credentials { get; init; } = [];
}

public sealed class ConnectCourierRequestValidator : AbstractValidator<ConnectCourierRequest>
{
    public ConnectCourierRequestValidator()
    {
        RuleFor(r => r.Credentials).NotEmpty().Must(c => c.Count <= 10).WithMessage("Too many credential fields.");
    }
}

/// <summary>
/// The shop's courier connections, made with the seller's own API keys. Keys go in and are
/// stored encrypted (ADR 0007); no endpoint ever returns them, only a masked hint.
/// </summary>
[ApiController]
[Route("couriers")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class CouriersController(ICourierAccountService couriers) : ControllerBase
{
    /// <summary>Couriers Hishab supports, the fields each needs, and which ones this shop has connected. All staff.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CourierConnection>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CourierConnection>>> List(CancellationToken cancellationToken) =>
        Ok(await couriers.ListAsync(cancellationToken));

    /// <summary>
    /// Connects a courier, or replaces its keys. The keys are checked with the courier first
    /// (400 if rejected). A new courier counts against the plan (402 on the Free plan's second courier). Owner and Manager (ManageShop).
    /// </summary>
    [HttpPut("{provider}")]
    [Authorize(Policy = Policies.CanManageShop)]
    [ProducesResponseType<CourierConnection>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status402PaymentRequired)]
    public async Task<ActionResult<CourierConnection>> Connect(
        CourierProvider provider, ConnectCourierRequest request, CancellationToken cancellationToken) =>
        Ok(await couriers.ConnectAsync(provider, request.Credentials, cancellationToken));

    /// <summary>Disconnects a courier and deletes its keys. Owner and Manager (ManageShop).</summary>
    [HttpDelete("{provider}")]
    [Authorize(Policy = Policies.CanManageShop)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Disconnect(CourierProvider provider, CancellationToken cancellationToken)
    {
        await couriers.DisconnectAsync(provider, cancellationToken);
        return NoContent();
    }
}
