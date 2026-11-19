using System.Globalization;
using System.Security.Cryptography;
using Hishab.Domain.Common;

namespace Hishab.Application.Shops.Auth;

/// <summary>Generates one-time passwords.</summary>
public static class OtpCode
{
    public const int Length = 6;

    /// <summary>A uniformly random 6-digit code from a cryptographic RNG (System.Random is predictable).</summary>
    public static string Generate() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
}

/// <summary>
/// Keyed hash of an OTP, bound to the phone it was sent to. Keyed (HMAC with a server
/// secret), because a plain hash of only a million possible codes is reversed instantly.
/// </summary>
public interface IOtpHasher
{
    string Hash(Phone phone, string code);
}

/// <summary>Delivers an OTP. Development logs it with the phone masked; production uses an SMS gateway (V2).</summary>
public interface IOtpSender
{
    Task SendAsync(Phone phone, string code, CancellationToken cancellationToken);
}
