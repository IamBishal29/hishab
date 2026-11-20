using Hishab.Application.Common.Tenancy;
using Hishab.Application.Shops;
using Hishab.Application.Shops.Auth;
using Hishab.Application.Shops.Staff;
using Hishab.Infrastructure.Identity;
using Hishab.Infrastructure.Persistence;
using Hishab.Infrastructure.Persistence.Tenancy;
using Hishab.Infrastructure.Shops;
using Hishab.Infrastructure.Shops.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        services.AddScoped<ITenantContext, TenantContext>();

        services.AddDbContext<HishabDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString)
            .UseSnakeCaseNamingConvention());

        services.TryAddSingleton(TimeProvider.System);

        services.AddIdentityCore<HishabUser>()
            .AddEntityFrameworkStores<HishabDbContext>();

        AddOtp(services, configuration);
        AddTokens(services, configuration);
        services.AddScoped<IShopProfileService, ShopProfileService>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddSingleton<IShopPlanLimits, PlaceholderPlanLimits>();

        // Same DbContext and connection string as the app, so "healthy" means the app can really reach its data.
        services.AddHealthChecks()
            .AddDbContextCheck<HishabDbContext>("postgres");

        return services;
    }

    private static void AddOtp(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OtpOptions>()
            .Bind(configuration.GetSection(OtpOptions.SectionName))
            .Validate(o => o.HasValidKey(), $"Otp:HashKey must be base64 of at least {OtpOptions.MinKeyBytes} bytes (set it with user-secrets).")
            .Validate(o => o.Delivery != OtpDelivery.None, "Otp:Delivery is not configured; only 'Log' exists until the SMS gateway (V2).")
            .ValidateOnStart();

        services.AddSingleton<IOtpHasher, HmacOtpHasher>();
        services.AddSingleton<IOtpSender, LogOtpSender>();
        services.AddScoped<IOtpAuthService, OtpAuthService>();
    }

    private static void AddTokens(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => o.HasValidKey(), $"Jwt:SigningKey must be base64 of at least {JwtOptions.MinKeyBytes} bytes (set it with user-secrets).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience), "Jwt:Issuer and Jwt:Audience are required.")
            .Validate(o => o.AccessTokenLifetime > TimeSpan.Zero && o.AccessTokenLifetime <= TimeSpan.FromHours(1), "Jwt:AccessTokenLifetime must be between 0 and 1 hour.")
            .Validate(o => o.RefreshTokenLifetime > o.AccessTokenLifetime, "Jwt:RefreshTokenLifetime must be longer than the access token lifetime.")
            .ValidateOnStart();

        services.AddScoped<IAuthTokenService, AuthTokenService>();
    }
}
