using Hishab.Application.Common.Auditing;
using Hishab.Application.Common.Tenancy;
using Hishab.Application.Couriers;
using Hishab.Application.Customers;
using Hishab.Application.Finance;
using Hishab.Application.Orders;
using Hishab.Application.Products;
using Hishab.Application.Shops;
using Hishab.Application.Shops.Auth;
using Hishab.Application.Shops.Staff;
using Hishab.Infrastructure.Couriers;
using Hishab.Infrastructure.Couriers.Fake;
using Hishab.Infrastructure.Couriers.Pathao;
using Hishab.Infrastructure.Customers;
using Hishab.Infrastructure.Finance;
using Hishab.Infrastructure.Identity;
using Hishab.Infrastructure.Jobs;
using Hishab.Infrastructure.Labels;
using Hishab.Infrastructure.Orders;
using Hishab.Infrastructure.Persistence;
using Hishab.Infrastructure.Persistence.Auditing;
using Hishab.Infrastructure.Persistence.Tenancy;
using Hishab.Infrastructure.Products;
using Hishab.Infrastructure.Shops;
using Hishab.Infrastructure.Shops.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
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

        services.TryAddScoped<IRequestContext, NoRequestContext>(); // the API registers the HTTP one
        services.AddScoped<AuditInterceptor>();
        services.AddDbContext<HishabDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));

        services.TryAddSingleton(TimeProvider.System);

        services.AddIdentityCore<HishabUser>()
            .AddEntityFrameworkStores<HishabDbContext>();

        AddOtp(services, configuration);
        AddTokens(services, configuration);
        services.AddScoped<IShopProfileService, ShopProfileService>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IPlanLimitService, PlanLimitService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ICustomerOrderHistory, CustomerOrderHistoryQuery>();
        services.AddScoped<ICodSummaryService, CodSummaryService>();
        AddCouriers(services, configuration);
        JobsSetup.AddJobs(services, configuration);
        services.AddSingleton<ChromiumPdfRenderer>(); // one browser per process (ADR 0008)
        services.AddScoped<ILabelService, LabelService>();
        services.AddScoped<IInvoiceService, InvoiceService>();

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

    private static void AddCouriers(IServiceCollection services, IConfiguration configuration)
    {
        // ADR 0007: key ring in Postgres, every key wrapped by a certificate from secrets.
        services.AddOptions<DataProtectionKeyOptions>()
            .Bind(configuration.GetSection(DataProtectionKeyOptions.SectionName))
            .Validate(o => o.TryLoadCertificate() is not null, "DataProtection:CertificateBase64 must be a base64 PFX with an RSA private key (set it with user-secrets; see README).")
            .ValidateOnStart();
        services.AddDataProtection()
            .SetApplicationName("Hishab") // all API instances share one key ring
            .PersistKeysToDbContext<HishabDbContext>();
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>, CertificateKeyEncryption>();
        services.AddSingleton<ICertificateResolver, ConfiguredCertificateResolver>();

        services.AddOptions<BookingOptions>()
            .Bind(configuration.GetSection(BookingOptions.SectionName))
            .Validate(o => o.IsValid(), "Booking:MaxBatchSize must be 1-500 and Booking:MaxParallelBookings 1-16.")
            .ValidateOnStart();
        services.AddSingleton<CourierBatchSender>();

        services.AddSingleton<FakeCourierControl>();
        services.AddSingleton<ICourierProvider, FakeCourierProvider>();
        PathaoSetup.AddPathao(services, configuration);
        services.AddSingleton<ICourierProviderRegistry, CourierProviderRegistry>();
        services.AddSingleton<ICourierCredentialProtector, CourierCredentialProtector>();
        services.AddScoped<ICourierAccountService, CourierAccountService>();
        services.AddScoped<IShipmentService, ShipmentService>();
        services.AddMemoryCache();
        services.AddScoped<ICourierLocationService, CourierLocationService>();
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
