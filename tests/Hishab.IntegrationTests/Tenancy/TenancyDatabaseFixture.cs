using Hishab.Application.Common.Tenancy;
using Hishab.Domain.Common;
using Hishab.Infrastructure.Persistence;
using Hishab.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Hishab.IntegrationTests.Tenancy;

/// <summary>Test-only tenant-owned entity, so the convention is tested before real entities exist.</summary>
public sealed class TestNote : ITenantOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TenantId { get; set; }

    public string Text { get; set; } = string.Empty;
}

/// <summary>The production context plus <see cref="TestNote"/>; the tenant convention picks it up automatically.</summary>
public sealed class TestTenantDbContext(DbContextOptions<TestTenantDbContext> options, ITenantContext tenantContext)
    : HishabDbContext(options, tenantContext)
{
    public DbSet<TestNote> Notes => Set<TestNote>();
}

public sealed class TenancyDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateContext(tenantId: null);
        await db.Database.EnsureCreatedAsync();
    }

    /// <summary>Creates a context for the given tenant; null means "no tenant set".</summary>
    public TestTenantDbContext CreateContext(Guid? tenantId)
    {
        var tenantContext = new TenantContext();
        if (tenantId is { } id)
        {
            tenantContext.SetTenant(id);
        }

        var options = new DbContextOptionsBuilder<TestTenantDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        return new TestTenantDbContext(options, tenantContext);
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();
}
