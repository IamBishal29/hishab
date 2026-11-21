using Hangfire;
using Hishab.Application.Common.Tenancy;
using Hishab.Domain.Couriers;
using Hishab.Infrastructure.Couriers;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hishab.Infrastructure.Jobs;

/// <summary>
/// Hangfire entry points for the courier status sync. A recurring dispatcher finds the shops with
/// parcels on the road and enqueues one job per (shop, courier); nothing to register or remove
/// when a shop connects or disconnects a courier. Each job names its tenant explicitly (there is no
/// signed-in user in a background job) and sets it on a fresh scope before touching any data.
/// Hangfire runs a job at least once, so every step is safe to repeat.
/// </summary>
public sealed partial class CourierStatusJobs(IServiceScopeFactory scopes, IBackgroundJobClient jobs, ILogger<CourierStatusJobs> logger)
{
    public const string RecurringJobId = "courier-status-sync";

    /// <summary>Runs on the schedule. No retries: the next run is minutes away and does the same.</summary>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task DispatchAsync(CancellationToken cancellationToken)
    {
        var work = await FindWorkAsync(cancellationToken);
        foreach (var (tenantId, provider) in work)
        {
            jobs.Enqueue<CourierStatusJobs>(j => j.SyncShopAsync(tenantId, provider, CancellationToken.None));
        }

        LogDispatched(logger, work.Count);
    }

    /// <summary>One shop's parcels with one courier, inside that shop's tenant.</summary>
    [AutomaticRetry(Attempts = 2)]
    public async Task SyncShopAsync(Guid tenantId, CourierProvider provider, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(tenantId); // first, before any query
        using var _ = logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenantId });

        var summary = await scope.ServiceProvider.GetRequiredService<CourierStatusSync>().SyncAsync(provider, cancellationToken);
        LogSynced(logger, provider, summary.Checked, summary.Moved, summary.Failed);
    }

    /// <summary>
    /// The (shop, courier) pairs with parcels on the road. Deliberately cross-tenant (named filter
    /// off): it decides which shops get a job and reads only IDs, never shop data.
    /// </summary>
    internal async Task<IReadOnlyList<(Guid TenantId, CourierProvider Provider)>> FindWorkAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HishabDbContext>();
        var pairs = await db.Shipments
            .IgnoreQueryFilters([HishabDbContext.TenantFilter])
            .Where(CourierStatusSync.IsTracking)
            .Select(s => new { s.TenantId, s.Provider })
            .Distinct()
            .ToListAsync(cancellationToken);
        return [.. pairs.Select(p => (p.TenantId, p.Provider))];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Courier status sync: {Count} shop jobs enqueued")]
    private static partial void LogDispatched(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Provider} status sync: {Checked} checked, {Moved} moved, {Failed} failed")]
    private static partial void LogSynced(ILogger logger, CourierProvider provider, int @checked, int moved, int failed);
}
