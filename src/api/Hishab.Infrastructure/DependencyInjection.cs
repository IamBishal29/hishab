using Hishab.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hishab.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ConnectionString), "Database:ConnectionString is required.")
            .ValidateOnStart();

        services.AddHealthChecks()
            .AddNpgSql(sp => sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString, name: "postgres");

        return services;
    }
}
