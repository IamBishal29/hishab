using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Hishab.Application.Common.Security;
using Hishab.Application.Common.Tenancy;
using Hishab.Application.Shops.Auth;
using Hishab.Domain.Common;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hishab.Infrastructure.Shops.Auth;

internal sealed partial class AuthTokenService(
    HishabDbContext db,
    ITenantContext tenant,
    IOptions<JwtOptions> options,
    TimeProvider clock,
    ILogger<AuthTokenService> logger) : IAuthTokenService
{
    private readonly JwtOptions _options = options.Value;

    public async Task<AuthTokens> IssueAsync(Guid userId, Guid tenantId, ShopRole role, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var (refreshToken, row) = NewRefreshToken(userId, familyId: Guid.NewGuid(), now);
        db.RefreshTokens.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return Pair(userId, tenantId, role, now, refreshToken, row.ExpiresAt);
    }

    public async Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var current = await FindAnyTenantAsync(refreshToken, cancellationToken) ?? throw SessionExpired();

        var result = current.Redeem(now);
        if (result == RefreshTokenRedemption.Reused)
        {
            await RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            LogReuseDetected(logger, current.FamilyId, current.UserId);
            throw SessionExpired();
        }

        if (result != RefreshTokenRedemption.Success)
        {
            throw SessionExpired();
        }

        // Re-read the user so a changed role (or a removed user) takes effect at the next refresh.
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == current.UserId, cancellationToken) ?? throw SessionExpired();

        var (nextToken, next) = NewRefreshToken(user.Id, current.FamilyId, now);
        db.RefreshTokens.Add(next);
        try
        {
            await db.SaveChangesAsync(cancellationToken); // marks the old token used and adds the next one atomically
        }
        catch (DbUpdateConcurrencyException)
        {
            throw SessionExpired(); // a parallel refresh with the same token won; its pair stays valid
        }

        return Pair(user.Id, user.TenantId, user.Role, now, nextToken, next.ExpiresAt);
    }

    public async Task RevokeFamilyAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var current = await FindAnyTenantAsync(refreshToken, cancellationToken);
        if (current is not null)
        {
            await RevokeFamilyAsync(current.FamilyId, clock.GetUtcNow(), cancellationToken);
        }
    }

    internal static string Hash(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    /// <summary>
    /// The refresh call is anonymous (the access token may have expired), so the tenant is not
    /// known yet. Like the login lookup, this switches off only the tenant filter, by name, and
    /// searches by a 256-bit secret; the row found then sets the tenant for the rest of the request.
    /// </summary>
    private async Task<RefreshToken?> FindAnyTenantAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > 128)
        {
            return null;
        }

        var hash = Hash(refreshToken);
        var token = await db.RefreshTokens
            .IgnoreQueryFilters([HishabDbContext.TenantFilter])
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null || (tenant.TenantId is { } signedIn && signedIn != token.TenantId))
        {
            return null; // a bearer token of another shop on the same request: refuse rather than mix tenants
        }

        tenant.SetTenant(token.TenantId);
        return token;
    }

    /// <summary>One set-based UPDATE; already revoked rows keep their original revocation time.</summary>
    private Task<int> RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);

    private (string Token, RefreshToken Row) NewRefreshToken(Guid userId, Guid familyId, DateTimeOffset now)
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (token, RefreshToken.Issue(userId, familyId, Hash(token), now, _options.RefreshTokenLifetime));
    }

    private AuthTokens Pair(Guid userId, Guid tenantId, ShopRole role, DateTimeOffset now, string refreshToken, DateTimeOffset refreshExpiresAt)
    {
        var expires = now + _options.AccessTokenLifetime;
        var accessToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(HishabClaimTypes.UserId, userId.ToString()),
                new Claim(HishabClaimTypes.TenantId, tenantId.ToString()),
                new Claim(HishabClaimTypes.Role, role.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ]),
            SigningCredentials = new SigningCredentials(_options.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
        });

        return new AuthTokens(accessToken, expires, refreshToken, refreshExpiresAt);
    }

    private static AuthenticationFailedException SessionExpired() => new(AuthTokenMessages.SessionExpired);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token reuse detected; revoked family {FamilyId} of user {UserId}")]
    private static partial void LogReuseDetected(ILogger logger, Guid familyId, Guid userId);
}
