using Hishab.Domain.Common;
using Hishab.Infrastructure.Persistence;
using Hishab.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Hishab.ArchitectureTests;

/// <summary>
/// Guards the production model without a database: a new tenant-owned entity that is not
/// mapped, or not filtered, fails the build instead of leaking data.
/// </summary>
public class TenancyModelTests
{
    private static HishabDbContext CreateContext() => new(
        new DbContextOptionsBuilder<HishabDbContext>()
            .UseNpgsql("Host=model-only") // never connects; only builds the model
            .UseSnakeCaseNamingConvention()
            .Options,
        new TenantContext());

    [Fact]
    public void Every_tenant_owned_entity_in_the_model_has_the_tenant_filter()
    {
        using var db = CreateContext();

        var unfiltered = db.Model.GetEntityTypes()
            .Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType) && t.BaseType is null && !t.IsOwned())
            .Where(t => t.FindDeclaredQueryFilter(HishabDbContext.TenantFilter) is null)
            .Select(t => t.ClrType.Name);

        unfiltered.ShouldBeEmpty();
    }

    [Fact]
    public void Every_tenant_owned_domain_type_is_mapped()
    {
        using var db = CreateContext();
        var mapped = db.Model.GetEntityTypes().Select(t => t.ClrType).ToHashSet();

        var unmapped = typeof(Domain.AssemblyMarker).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ITenantOwned).IsAssignableFrom(t))
            .Where(t => !mapped.Contains(t))
            .Select(t => t.Name);

        unmapped.ShouldBeEmpty();
    }
}
