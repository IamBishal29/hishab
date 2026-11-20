using FluentValidation;
using Hishab.Api.Auth;
using Hishab.Application.Shops.Staff;
using Hishab.Domain.Shops;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hishab.Api.Controllers;

public sealed class InviteStaffRequest
{
    /// <summary>Bangladeshi mobile number, any common spelling.</summary>
    /// <example>01712345678</example>
    public string Phone { get; init; } = string.Empty;

    /// <summary>Manager, Moderator or Packer.</summary>
    /// <example>Moderator</example>
    public ShopRole Role { get; init; }
}

public sealed class ChangeStaffRoleRequest
{
    /// <summary>Manager, Moderator or Packer.</summary>
    /// <example>Packer</example>
    public ShopRole Role { get; init; }
}

public sealed class InviteStaffRequestValidator : AbstractValidator<InviteStaffRequest>
{
    public InviteStaffRequestValidator()
    {
        RuleFor(r => r.Phone).NotEmpty().MaximumLength(20);
        RuleFor(r => r.Role).Must(ShopStaffRules.IsAssignable).WithMessage("Choose Manager, Moderator or Packer.");
    }
}

public sealed class ChangeStaffRoleRequestValidator : AbstractValidator<ChangeStaffRoleRequest>
{
    public ChangeStaffRoleRequestValidator() =>
        RuleFor(r => r.Role).Must(ShopStaffRules.IsAssignable).WithMessage("Choose Manager, Moderator or Packer.");
}

/// <summary>The shop's team. Owner only.</summary>
[ApiController]
[Route("staff")]
[Authorize(Policy = Policies.CanManageStaff)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class StaffController(IStaffService staff) : ControllerBase
{
    /// <summary>Lists everyone in the shop, owner included, oldest first.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<StaffMember>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StaffMember>>> List(CancellationToken cancellationToken) =>
        Ok(await staff.ListAsync(cancellationToken));

    /// <summary>Invites a person by phone; they join the shop at their next OTP sign-in.</summary>
    [HttpPost]
    [ProducesResponseType<StaffMember>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffMember>> Invite(InviteStaffRequest request, CancellationToken cancellationToken)
    {
        var member = await staff.InviteAsync(request.Phone, request.Role, cancellationToken);
        return Created($"/api/staff/{member.Id}", member);
    }

    /// <summary>Changes a staff member's role. Takes effect at their next token refresh (within 15 minutes).</summary>
    [HttpPut("{id:guid}/role")]
    [ProducesResponseType<StaffMember>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<StaffMember>> ChangeRole(Guid id, ChangeStaffRoleRequest request, CancellationToken cancellationToken) =>
        Ok(await staff.ChangeRoleAsync(id, request.Role, cancellationToken));

    /// <summary>Removes a staff member and ends their sessions. The owner cannot be removed.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        await staff.RemoveAsync(id, cancellationToken);
        return NoContent();
    }
}
