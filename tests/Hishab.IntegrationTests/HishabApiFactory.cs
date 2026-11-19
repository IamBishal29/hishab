using Hishab.Infrastructure.Persistence;
using Hishab.IntegrationTests.Support;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Testcontainers.PostgreSql;

namespace Hishab.IntegrationTests;

/// <summary>
/// Boots the real API against a throwaway PostgreSQL container with migrations applied,
/// plus test-only endpoints, a header-based test login and a log capture sink.
/// </summary>
public sealed class HishabApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public CapturingLogSink Logs { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await Services.MigrateDatabaseAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ConnectionString", _postgres.GetConnectionString());
        builder.UseSetting("Observability:JsonConsole", "false");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILogEventSink>(Logs);
            services.AddControllers().AddApplicationPart(typeof(HishabApiFactory).Assembly);
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
