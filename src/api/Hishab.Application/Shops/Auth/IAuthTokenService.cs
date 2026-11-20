using Hishab.Domain.Shops;

namespace Hishab.Application.Shops.Auth;

/// <summary>Issues short-lived access tokens and rotating, one-time refresh tokens.</summary>
public interface IAuthTokenService
{
    /// <summary>Starts a new token family after a successful login. The tenant must already be set.</summary>
    Task<AuthTokens> IssueAsync(Guid userId, Guid tenantId, ShopRole role, CancellationToken cancellationToken);

    /// <summary>
    /// Redeems a refresh token and returns the next pair in its family. Presenting a token
    /// that was already redeemed revokes the whole family (it was copied).
    /// </summary>
    /// <exception cref="Domain.Common.AuthenticationFailedException">Unknown, expired, revoked or reused token.</exception>
    Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Revokes the token's family (logout). Unknown tokens are ignored.</summary>
    Task RevokeFamilyAsync(string refreshToken, CancellationToken cancellationToken);
}

public sealed record AuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public static class AuthTokenMessages
{
    public const string SessionExpired = "Your session has expired. Please sign in again.";
}
