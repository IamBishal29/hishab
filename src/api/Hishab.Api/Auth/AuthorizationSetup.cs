using Hishab.Application.Common.Security;
using Hishab.Domain.Shops;
using Microsoft.AspNetCore.Authorization;

namespace Hishab.Api.Auth;

/// <summary>
/// Named policies, one per <see cref="Permission"/>: endpoints ask for a capability
/// (<c>[Authorize(Policy = Policies.CanEnterOrders)]</c>), never for a list of roles.
/// Which roles hold which permission lives only in <see cref="RolePermissions"/>.
/// </summary>
public static class Policies
{
    public const string CanManageShop = "Can" + nameof(Permission.ManageShop);
    public const string CanManageStaff = "Can" + nameof(Permission.ManageStaff);
    public const string CanEnterOrders = "Can" + nameof(Permission.EnterOrders);
    public const string CanPack = "Can" + nameof(Permission.Pack);
    public const string CanSeeProfit = "Can" + nameof(Permission.SeeProfit);

    public static string For(Permission permission) => "Can" + permission;

    public static IServiceCollection AddHishabAuthorization(this IServiceCollection services)
    {
        var builder = services.AddAuthorizationBuilder();
        foreach (var permission in Enum.GetValues<Permission>())
        {
            builder.AddPolicy(For(permission), policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission)));
        }

        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
        return services;
    }
}

public sealed record PermissionRequirement(Permission Permission) : IAuthorizationRequirement;

/// <summary>
/// Maps the signed <c>role</c> claim to permissions at request time. The token carries the
/// role only, so changing the matrix takes effect at once, without re-issuing tokens.
/// </summary>
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (Enum.TryParse<ShopRole>(context.User.FindFirst(HishabClaimTypes.Role)?.Value, ignoreCase: false, out var role)
            && Enum.IsDefined(role)
            && RolePermissions.Has(role, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask; // not Fail(): other handlers may still grant it; no success means 403
    }
}
