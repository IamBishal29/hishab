using System.Globalization;
using Hishab.Domain.Common;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Identity;
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
    public async Task Users_are_isolated_between_shops()
    {
        await CrossTenantAssert.IsIsolatedAsync(
            id => fixture.CreateContext(id),
            () =>
            {
                var phone = "+8801" + Random.Shared.Next(300_000_000, 999_999_999).ToString(CultureInfo.InvariantCulture);
                return new HishabUser { Id = Guid.NewGuid(), UserName = phone, NormalizedUserName = phone, PhoneNumber = phone, Role = ShopRole.Owner };
            },
            Ct,
            newTenant: async () =>
            {
                var shop = Shop.Create(DateTimeOffset.UtcNow); // users have a foreign key to shops
                await using var db = fixture.CreateContext(tenantId: null);
                db.Shops.Add(shop);
                await db.SaveChangesAsync(Ct);
                return shop.Id;
            });
    }

    [Fact]
    public async Task Refresh_tokens_are_isolated_between_shops()
    {
        var owners = new List<Guid>();
        await CrossTenantAssert.IsIsolatedAsync(
            id => fixture.CreateContext(id),
            () => RefreshToken.Issue(owners[0], Guid.NewGuid(), Convert.ToHexString(Guid.NewGuid().ToByteArray()), DateTimeOffset.UtcNow, TimeSpan.FromDays(30)),
            Ct,
            newTenant: async () =>
            {
                var shop = Shop.Create(DateTimeOffset.UtcNow); // refresh tokens have a foreign key to users, users to shops
                await using (var db = fixture.CreateContext(tenantId: null))
                {
                    db.Shops.Add(shop);
                    await db.SaveChangesAsync(Ct);
                }

                var phone = "+8801" + Random.Shared.Next(300_000_000, 999_999_999).ToString(CultureInfo.InvariantCulture);
                var owner = new HishabUser { Id = Guid.NewGuid(), UserName = phone, NormalizedUserName = phone, PhoneNumber = phone, Role = ShopRole.Owner };
                await using (var db = fixture.CreateContext(shop.Id))
                {
                    db.Users.Add(owner);
                    await db.SaveChangesAsync(Ct);
                }

                owners.Add(owner.Id); // the token is created for tenant A, the first shop
                return shop.Id;
            });
    }

    [Fact]
    public async Task Audit_entries_are_isolated_between_shops()
    {
        await CrossTenantAssert.IsIsolatedAsync(
            id => fixture.CreateContext(id),
            () => AuditEntry.Record(Guid.Empty, Guid.NewGuid(), "Shop", Guid.NewGuid().ToString(), AuditAction.Updated, """{"name":{"old":"A","new":"B"}}""", "corr-1", DateTimeOffset.UtcNow),
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
