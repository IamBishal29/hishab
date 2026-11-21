using System.Security.Cryptography;
using Hishab.Application.Shops.Auth;
using Hishab.Infrastructure.Persistence;
using Hishab.Infrastructure.Persistence.Tenancy;
using Hishab.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Serilog.Core;
using Testcontainers.PostgreSql;

namespace Hishab.IntegrationTests;

/// <summary>
/// Boots the real API against a throwaway PostgreSQL container with migrations applied,
/// plus test-only endpoints, log capture, OTP capture and a controllable clock. Tests sign
/// in for real (OTP, then JWT) through <see cref="AuthSession"/>.
/// </summary>
public class HishabApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public CapturingLogSink Logs { get; } = new();

    public CapturingOtpSender Otps { get; } = new();

    /// <summary>Starts at the real current time; tests advance it to check expiry.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>OTP requests per IP per minute. High by default so tests do not throttle each other.</summary>
    protected virtual int OtpPermitsPerMinute => 10_000;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        // Migrate before the host starts, as Program does: hosted services (the Data Protection
        // key ring loader) read tables at startup.
        var options = new DbContextOptionsBuilder<HishabDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var db = new HishabDbContext(options, new TenantContext()))
        {
            await db.Database.MigrateAsync();
        }

        _ = Services; // starts the host
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ConnectionString", _postgres.GetConnectionString());
        builder.UseSetting("Observability:JsonConsole", "false");
        builder.UseSetting("Otp:HashKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))); // fresh per run, never committed
        builder.UseSetting("Otp:Delivery", "Log");
        builder.UseSetting("Jobs:RunServer", "false"); // tests run jobs directly: no worker threads racing the assertions
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("DataProtection:CertificateBase64", Support.TestCertificate.PfxBase64); // fresh per run, in memory only
        builder.UseSetting("RateLimiting:OtpPermitsPerMinute", OtpPermitsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILogEventSink>(Logs);
            services.RemoveAll<IOtpSender>();
            services.AddSingleton<IOtpSender>(Otps);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddControllers().AddApplicationPart(typeof(HishabApiFactory).Assembly);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Same app with a tiny IP limit, to test the rate limiter itself.</summary>
public sealed class LowRateLimitApiFactory : HishabApiFactory
{
    protected override int OtpPermitsPerMinute => 2;
}
