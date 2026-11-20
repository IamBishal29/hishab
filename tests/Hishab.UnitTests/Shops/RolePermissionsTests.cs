using Hishab.Domain.Shops;

namespace Hishab.UnitTests.Shops;

public class RolePermissionsTests
{
    [Fact]
    public void Owner_has_every_permission()
    {
        RolePermissions.For(ShopRole.Owner).ShouldBe(Enum.GetValues<Permission>(), ignoreOrder: true);
    }

    [Theory]
    [InlineData(ShopRole.Manager, new[] { Permission.ManageShop, Permission.EnterOrders, Permission.Pack })]
    [InlineData(ShopRole.Moderator, new[] { Permission.EnterOrders })]
    [InlineData(ShopRole.Packer, new[] { Permission.Pack })]
    public void Staff_roles_get_only_what_their_job_needs(ShopRole role, Permission[] expected)
    {
        RolePermissions.For(role).ShouldBe(expected, ignoreOrder: true);
    }

    [Theory]
    [InlineData(ShopRole.Manager)]
    [InlineData(ShopRole.Moderator)]
    [InlineData(ShopRole.Packer)]
    public void Only_the_owner_sees_profit_manages_staff_and_reads_the_audit_log(ShopRole role)
    {
        RolePermissions.Has(role, Permission.SeeProfit).ShouldBeFalse();
        RolePermissions.Has(role, Permission.ManageStaff).ShouldBeFalse();
        RolePermissions.Has(role, Permission.ViewAudit).ShouldBeFalse();
    }

    [Fact]
    public void Unknown_role_has_no_permissions()
    {
        RolePermissions.Has((ShopRole)99, Permission.Pack).ShouldBeFalse();
    }

    [Fact]
    public void Owner_role_cannot_be_assigned_to_staff()
    {
        ShopStaffRules.IsAssignable(ShopRole.Owner).ShouldBeFalse();
        ShopStaffRules.IsAssignable(ShopRole.Packer).ShouldBeTrue();
    }
}
