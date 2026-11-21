using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Hishab.Infrastructure.Jobs;

/// <summary>
/// Unhealthy when job storage cannot be read; Degraded when this instance should run jobs but no
/// server has sent a heartbeat lately (the API still serves requests, but statuses stop syncing).
/// </summary>
internal sealed class HangfireHealthCheck(JobStorage storage, IOptions<JobsOptions> options, TimeProvider clock) : IHealthCheck
{
    private static readonly TimeSpan _heartbeatTimeout = TimeSpan.FromMinutes(2);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var monitoring = storage.GetMonitoringApi();
        var servers = monitoring.Servers(); // reads storage: throws (Unhealthy) if it is unreachable
        var data = new Dictionary<string, object> { ["failedJobs"] = monitoring.FailedCount() };
        if (!options.Value.RunServer)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Jobs are processed by other instances.", data));
        }

        var since = clock.GetUtcNow().UtcDateTime - _heartbeatTimeout;
        var alive = servers.Count(s => s.Heartbeat >= since);
        data["servers"] = alive;
        return Task.FromResult(alive > 0
            ? HealthCheckResult.Healthy(data: data)
            : HealthCheckResult.Degraded("No job server heartbeat in the last 2 minutes.", data: data));
    }
}
