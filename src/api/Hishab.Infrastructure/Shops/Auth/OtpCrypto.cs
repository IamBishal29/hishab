using System.Security.Cryptography;
using System.Text;
using Hishab.Application.Shops.Auth;
using Hishab.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hishab.Infrastructure.Shops.Auth;

/// <summary>HMAC-SHA256 of "phone|code" with a server secret; the database alone cannot reverse it.</summary>
internal sealed class HmacOtpHasher(IOptions<OtpOptions> options) : IOtpHasher
{
    private readonly byte[] _key = Convert.FromBase64String(options.Value.HashKey);

    public string Hash(Phone phone, string code) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{phone.Value}|{code}")));
}

/// <summary>Development and test delivery: writes the code to the log with the phone masked.</summary>
internal sealed partial class LogOtpSender(ILogger<LogOtpSender> logger) : IOtpSender
{
    public Task SendAsync(Phone phone, string code, CancellationToken cancellationToken)
    {
        LogCode(logger, code, phone.Masked);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "DEV ONLY: OTP {Code} for {Phone}")]
    private static partial void LogCode(ILogger logger, string code, string phone);
}
