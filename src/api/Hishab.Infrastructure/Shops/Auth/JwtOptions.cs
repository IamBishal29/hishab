using Microsoft.IdentityModel.Tokens;

namespace Hishab.Infrastructure.Shops.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinKeyBytes = 32; // HS256 needs a key at least as long as its 256-bit output

    /// <summary>Base64 HMAC-SHA256 signing key. From user-secrets or environment variables only.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "hishab";

    public string Audience { get; set; } = "hishab-api";

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Tolerance for clock drift between servers; kept small because access tokens are short.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    internal bool HasValidKey()
    {
        var buffer = new byte[SigningKey.Length];
        return Convert.TryFromBase64String(SigningKey, buffer, out var length) && length >= MinKeyBytes;
    }

    public SymmetricSecurityKey CreateSigningKey() => new(Convert.FromBase64String(SigningKey));
}
