using System.Collections.Concurrent;
using Hishab.Application.Shops.Auth;
using Hishab.Domain.Common;

namespace Hishab.IntegrationTests.Support;

/// <summary>Replaces SMS/log delivery so tests can read the code that was "sent".</summary>
public sealed class CapturingOtpSender : IOtpSender
{
    private readonly ConcurrentDictionary<string, string> _latest = new();

    public Task SendAsync(Phone phone, string code, CancellationToken cancellationToken)
    {
        _latest[phone.Value] = code;
        return Task.CompletedTask;
    }

    public string LatestCodeFor(string phone) => _latest[Phone.Parse(phone).Value];
}
