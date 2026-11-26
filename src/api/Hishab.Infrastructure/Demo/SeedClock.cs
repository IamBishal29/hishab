namespace Hishab.Infrastructure.Demo;

/// <summary>
/// The app's clock while demo seeding is enabled. Normally it just passes the real time through;
/// during seeding it "travels" to each step of the demo timeline, so orders, history, bookings and
/// payouts are written by the real services as if they happened on those days (a believable 30-day
/// chart). Seeding finishes before the host serves requests or runs jobs, and always travels back.
/// </summary>
public sealed class SeedClock(TimeProvider inner) : TimeProvider
{
    private DateTimeOffset? _travelledTo;

    /// <summary>The real time, whatever the seeder is doing.</summary>
    public DateTimeOffset RealNow => inner.GetUtcNow();

    public override DateTimeOffset GetUtcNow() => _travelledTo ?? inner.GetUtcNow();

    public override long GetTimestamp() => inner.GetTimestamp();

    public override long TimestampFrequency => inner.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        inner.CreateTimer(callback, state, dueTime, period);

    internal void TravelTo(DateTimeOffset at) => _travelledTo = at;

    internal void ReturnToPresent() => _travelledTo = null;
}
