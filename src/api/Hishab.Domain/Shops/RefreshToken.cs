using Hishab.Domain.Common;

namespace Hishab.Domain.Shops;

public enum RefreshTokenRedemption
{
    Success,
    Expired,
    Revoked,

    /// <summary>Already exchanged once: someone holds a copy. The caller must revoke the family.</summary>
    Reused,
}

/// <summary>
/// One link in a refresh token chain. Each login starts a family; every refresh redeems the
/// current token once and issues the next one in the same family. Only a SHA-256 hash of
/// the token is stored, so a database leak does not hand out sessions.
/// </summary>
public sealed class RefreshToken : ITenantOwned
{
    // For EF Core materialisation only; domain code uses Issue().
    private RefreshToken() => TokenHash = null!;

    private RefreshToken(Guid userId, Guid familyId, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; set; }

    public Guid UserId { get; private set; }

    /// <summary>Shared by every token descended from one login; revoked as a unit.</summary>
    public Guid FamilyId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public static RefreshToken Issue(Guid userId, Guid familyId, string tokenHash, DateTimeOffset now, TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lifetime, TimeSpan.Zero);
        return new RefreshToken(userId, familyId, tokenHash, now, now + lifetime);
    }

    /// <summary>Exchanges the token once. Order matters: reuse is reported even after expiry or revocation.</summary>
    public RefreshTokenRedemption Redeem(DateTimeOffset now)
    {
        if (UsedAt is not null)
        {
            return RefreshTokenRedemption.Reused;
        }

        if (RevokedAt is not null)
        {
            return RefreshTokenRedemption.Revoked;
        }

        if (now >= ExpiresAt)
        {
            return RefreshTokenRedemption.Expired;
        }

        UsedAt = now;
        return RefreshTokenRedemption.Success;
    }
}
