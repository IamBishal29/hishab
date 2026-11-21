namespace Hishab.Infrastructure.Jobs;

/// <summary>Background jobs (section "Jobs"). Checked at startup.</summary>
public sealed class JobsOptions
{
    public const string SectionName = "Jobs";

    /// <summary>
    /// Whether this instance processes jobs. Every instance can enqueue; turn this off on web-only
    /// instances (and in tests, which run jobs directly and deterministically).
    /// </summary>
    public bool RunServer { get; set; } = true;

    public int WorkerCount { get; set; } = 2;

    /// <summary>How often the courier status sync runs (cron, UTC; Hangfire rejects a bad one at startup).</summary>
    public string StatusSyncCron { get; set; } = "*/15 * * * *";

    /// <summary>Most parcels one shop's sync asks about per run; the least recently checked go first.</summary>
    public int StatusSyncBatchSize { get; set; } = 200;

    public bool IsValid() =>
        WorkerCount is >= 1 and <= 20
        && StatusSyncBatchSize is >= 1 and <= 1_000;
}
