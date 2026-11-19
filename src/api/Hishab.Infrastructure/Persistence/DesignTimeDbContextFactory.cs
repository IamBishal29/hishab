using Hishab.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hishab.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> to build the model for migrations. It never connects,
/// so the placeholder connection string holds no secret.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<HishabDbContext>
{
    public HishabDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HishabDbContext>()
            .UseNpgsql("Host=design-time-only")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new HishabDbContext(options, new TenantContext());
    }
}
