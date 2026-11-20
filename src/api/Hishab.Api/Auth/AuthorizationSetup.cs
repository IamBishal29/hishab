using Hishab.Domain.Shops;

namespace Hishab.Api.Auth;

/// <summary>Named policies: endpoints ask for a capability, not a list of roles. The full staff matrix arrives in #8.</summary>
public static class Policies
{
    /// <summary>View and edit the shop profile and delivery charges.</summary>
    public const string ManageShop = nameof(ManageShop);

    public static IServiceCollection AddHishabAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(ManageShop, policy => policy.RequireRole(nameof(ShopRole.Owner), nameof(ShopRole.Manager)));
        return services;
    }
}
