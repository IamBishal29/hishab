namespace Hishab.Infrastructure.Shops.Auth;

public enum OtpDelivery
{
    /// <summary>Not configured: the app refuses to start (no silent OTP black hole in production).</summary>
    None,

    /// <summary>Write the code to the log (Development and tests only). SMS gateway arrives in V2.</summary>
    Log,
}

public sealed class OtpOptions
{
    public const string SectionName = "Otp";
    public const int MinKeyBytes = 32;

    /// <summary>Base64 HMAC key, at least 32 bytes. From user-secrets or environment variables only.</summary>
    public string HashKey { get; set; } = string.Empty;

    public OtpDelivery Delivery { get; set; }

    internal bool HasValidKey()
    {
        var buffer = new byte[HashKey.Length];
        return Convert.TryFromBase64String(HashKey, buffer, out var length) && length >= MinKeyBytes;
    }
}
