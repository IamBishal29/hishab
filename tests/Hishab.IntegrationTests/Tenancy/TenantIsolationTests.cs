using Hishab.Infrastructure.Persistence;
using Hishab.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Hishab.IntegrationTests.Tenancy;

public class TenantIsolationTests(TenancyDatabaseFixture fixture) : IClassFixture<TenancyDatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Tenant_filter_is_applied_by_convention_to_tenant_owned_entities()
    {
        using var db = fixture.CreateContext(tenantId: null);

        db.Model.FindEntityType(typeof(TestNote))!.FindDeclaredQueryFilter(HishabDbContext.TenantFilter).ShouldNotBeNull();
    }

    [Fact]
    public async Task Tenant_owned_rows_are_isolated_between_tenants()
    {
        await CrossTenantAssert.IsIsolatedAsync(
            id => fixture.CreateContext(id),
            () => new TestNote { Text = "Order from Mirpur" },
            Ct);
    }

    [Fact]
    public async Task Queries_return_nothing_when_no_tenant_is_set()
    {
        await using (var db = fixture.CreateContext(Guid.NewGuid()))
        {
            db.Notes.Add(new TestNote { Text = "seed" });
            await db.SaveChangesAsync(Ct);
        }

        await using var noTenant = fixture.CreateContext(tenantId: null);

        (await noTenant.Notes.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Saving_without_a_tenant_throws()
    {
        await using var db = fixture.CreateContext(tenantId: null);
        db.Notes.Add(new TestNote { Text = "orphan" });

        await Should.ThrowAsync<CrossTenantWriteException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Adding_a_row_for_another_tenant_throws()
    {
        await using var db = fixture.CreateContext(Guid.NewGuid());
        db.Notes.Add(new TestNote { TenantId = Guid.NewGuid(), Text = "smuggled" });

        await Should.ThrowAsync<CrossTenantWriteException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Moving_a_row_to_another_tenant_throws()
    {
        var tenant = Guid.NewGuid();
        await using var db = fixture.CreateContext(tenant);
        var note = new TestNote { Text = "mine" };
        db.Notes.Add(note);
        await db.SaveChangesAsync(Ct);

        note.TenantId = Guid.NewGuid();

        await Should.ThrowAsync<CrossTenantWriteException>(() => db.SaveChangesAsync(Ct));
    }
}
