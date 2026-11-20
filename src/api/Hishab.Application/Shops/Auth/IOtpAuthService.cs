using Hishab.Domain.Shops;

namespace Hishab.Application.Shops.Auth;

/// <summary>Phone + OTP sign-up and login.</summary>
public interface IOtpAuthService
{
    /// <summary>
    /// Sends a code if the phone is valid and within the send limits. The result is the
    /// same for registered and unknown phones, so callers cannot probe for accounts.
    /// </summary>
    /// <exception cref="Domain.Common.DomainValidationException">Invalid phone.</exception>
    /// <exception cref="Domain.Common.TooManyRequestsException">Send limit reached.</exception>
    Task<OtpSent> RequestOtpAsync(string phone, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies the latest code. Every failure (wrong, expired, locked, used, no code)
    /// raises the same validation error. A new phone creates its shop and Owner user.
    /// </summary>
    Task<OtpLogin> VerifyOtpAsync(string phone, string code, CancellationToken cancellationToken);
}

public sealed record OtpSent(int ExpiresInSeconds, int ResendAfterSeconds);

public sealed record OtpLogin(Guid UserId, Guid ShopId, ShopRole Role, bool IsNewShop);

/// <summary>Per-phone send limits. Together with the 5-attempt lockout they bound guesses to 15 per 15 minutes.</summary>
public static class OtpSendPolicy
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public const int MaxPerWindow = 3;

    public const string InvalidCodeMessage = "The code is invalid or has expired. Request a new code and try again.";
}
