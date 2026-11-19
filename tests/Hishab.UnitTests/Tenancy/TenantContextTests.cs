using Hishab.Infrastructure.Persistence.Tenancy;

namespace Hishab.UnitTests.Tenancy;

public class TenantContextTests
{
    [Fact]
    public void Tenant_is_null_until_set()
    {
        new TenantContext().TenantId.ShouldBeNull();
    }

    [Fact]
    public void SetTenant_rejects_an_empty_id()
    {
        Should.Throw<ArgumentException>(() => new TenantContext().SetTenant(Guid.Empty));
    }

    [Fact]
    public void SetTenant_is_idempotent_for_the_same_tenant()
    {
        var tenant = Guid.NewGuid();
        var context = new TenantContext();

        context.SetTenant(tenant);
        context.SetTenant(tenant);

        context.TenantId.ShouldBe(tenant);
    }

    [Fact]
    public void Switching_to_another_tenant_within_a_scope_throws()
    {
        var context = new TenantContext();
        context.SetTenant(Guid.NewGuid());

        Should.Throw<InvalidOperationException>(() => context.SetTenant(Guid.NewGuid()));
    }
}
