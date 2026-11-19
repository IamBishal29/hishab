using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Hishab.IntegrationTests.Support;

/// <summary>Collects every Serilog event the app writes (picked up through ReadFrom.Services).</summary>
public sealed class CapturingLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

    /// <summary>
    /// Waits for one event of a request (matched by correlation ID, since tests share the sink).
    /// Some events, like the request summary, are written after the client already has the
    /// response, so a single read would race the logger.
    /// </summary>
    public async Task<LogEvent> WaitForAsync(string correlationId, Func<LogEvent, bool> predicate, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var match = _events.FirstOrDefault(e => HasCorrelationId(e, correlationId) && predicate(e));
            if (match is not null)
            {
                return match;
            }

            if (DateTime.UtcNow > deadline)
            {
                var seen = string.Join("; ", _events
                    .Where(e => HasCorrelationId(e, correlationId) || predicate(e))
                    .Select(e => $"{e.MessageTemplate.Text} [{string.Join(",", e.Properties.Keys)}]"));
                throw new TimeoutException(
                    $"No matching log event for correlation ID {correlationId} within 5 seconds. Related events: {seen}");
            }

            await Task.Delay(20, cancellationToken);
        }
    }

    private static bool HasCorrelationId(LogEvent e, string correlationId) =>
        e.Properties.TryGetValue("CorrelationId", out var v) && v is ScalarValue { Value: string s } && s == correlationId;
}
