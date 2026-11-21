using Hangfire;
using Hangfire.PostgreSql;
using Hishab.Infrastructure.Couriers;
using Hishab.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Hishab.Infrastructure.Jobs;

internal static class JobsSetup
{
    public static void AddJobs(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JobsOptions>()
            .Bind(configuration.GetSection(JobsOptions.SectionName))
            .Validate(o => o.IsValid(), "Jobs: WorkerCount 1-20 and StatusSyncBatchSize 1-1000 are required.")
            .ValidateOnStart();

        // Same database, own schema: one backup, one connection string, and jobs commit next to our data.
        services.AddHangfire((sp, config) => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                o => o.UseNpgsqlConnection(sp.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString),
                new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));

        services.AddScoped<CourierStatusSync>();
        services.AddSingleton<CourierStatusJobs>();
        services.AddHealthChecks().AddCheck<HangfireHealthCheck>("hangfire");

        // Read once at startup: whether this instance is a job worker is a deployment decision.
        if (configuration.GetValue<bool?>($"{JobsOptions.SectionName}:{nameof(JobsOptions.RunServer)}") ?? true)
        {
            services.AddHangfireServer((sp, server) => server.WorkerCount = sp.GetRequiredService<IOptions<JobsOptions>>().Value.WorkerCount);
            services.AddHostedService<RecurringJobsRegistration>();
        }
    }
}

/// <summary>Creates or updates the recurring jobs at startup (idempotent: same ID, latest schedule wins).</summary>
internal sealed class RecurringJobsRegistration(IRecurringJobManager recurring, IOptions<JobsOptions> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurring.AddOrUpdate<CourierStatusJobs>(
            CourierStatusJobs.RecurringJobId,
            j => j.DispatchAsync(CancellationToken.None),
            options.Value.StatusSyncCron,
            new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
