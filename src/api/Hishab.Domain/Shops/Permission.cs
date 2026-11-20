using System.Diagnostics.CodeAnalysis;

namespace Hishab.Domain.Shops;

/// <summary>What a user may do. Endpoints require permissions, never roles.</summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Domain term; not a .NET code access security permission.")]
public enum Permission
{
    /// <summary>Edit the shop profile and delivery charges.</summary>
    ManageShop,

    /// <summary>Invite, re-role and remove staff.</summary>
    ManageStaff,

    /// <summary>Create and edit orders and customers.</summary>
    EnterOrders,

    /// <summary>Pack, book couriers and print labels.</summary>
    Pack,

    /// <summary>See cost prices, profit and payouts (PRD: only the Owner sees profit).</summary>
    SeeProfit,

    /// <summary>Read the audit log (who changed what).</summary>
    ViewAudit,
}

/// <summary>
/// The one place that maps roles to permissions (least privilege: each role gets only what
/// its job needs). Fixed in code for the MVP; customisable roles would move this map into a
/// per-shop table without touching any endpoint.
/// </summary>
public static class RolePermissions
{
    private static readonly Dictionary<ShopRole, HashSet<Permission>> _map = new()
    {
        [ShopRole.Owner] = [.. Enum.GetValues<Permission>()],
        [ShopRole.Manager] = [Permission.ManageShop, Permission.EnterOrders, Permission.Pack],
        [ShopRole.Moderator] = [Permission.EnterOrders],
        [ShopRole.Packer] = [Permission.Pack],
    };

    public static bool Has(ShopRole role, Permission permission) =>
        _map.TryGetValue(role, out var permissions) && permissions.Contains(permission);

    public static IReadOnlyCollection<Permission> For(ShopRole role) =>
        _map.TryGetValue(role, out var permissions) ? permissions : [];
}

/// <summary>Rules for a shop's team.</summary>
public static class ShopStaffRules
{
    /// <summary>Roles an Owner can give: everything except Owner (one owner per shop; ownership transfer is later).</summary>
    public static bool IsAssignable(ShopRole role) => role is ShopRole.Manager or ShopRole.Moderator or ShopRole.Packer;
}
