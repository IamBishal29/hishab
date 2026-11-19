using System.Security.Cryptography;
using System.Text;
using Hishab.Domain.Common;

namespace Hishab.Domain.Shops;

public enum OtpChallengeStatus
{
    Pending,
    Verified,
    Locked,
}

public enum OtpVerificationResult
{
    Success,
    Invalid,
    Expired,
    Locked,
    AlreadyUsed,
}

/// <summary>
/// One one-time password sent to one phone. Only a keyed hash of the code is stored.
/// Valid for <see cref="Lifetime"/>, locked after <see cref="MaxAttempts"/> wrong codes,
/// and single-use. Not tenant-owned: it exists before the user has a shop.
/// </summary>
public sealed class OtpChallenge
{
    public const int MaxAttempts = 5;

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private OtpChallenge(Phone phone, string codeHash, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        Phone = phone;
        CodeHash = codeHash;
        CreatedAt = now;
        ExpiresAt = now + Lifetime;
        Status = OtpChallengeStatus.Pending;
    }

    public Guid Id { get; private set; }

    public Phone Phone { get; private set; }

    public string CodeHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public int FailedAttempts { get; private set; }

    public OtpChallengeStatus Status { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public static OtpChallenge Issue(Phone phone, string codeHash, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(phone);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);
        return new OtpChallenge(phone, codeHash, now);
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>
    /// Checks a code (already hashed with the same key). Callers must not reveal which
    /// failure happened: the API answers every non-success the same way.
    /// </summary>
    public OtpVerificationResult Verify(string attemptHash, DateTimeOffset now)
    {
        switch (Status)
        {
            case OtpChallengeStatus.Verified:
                return OtpVerificationResult.AlreadyUsed;
            case OtpChallengeStatus.Locked:
                return OtpVerificationResult.Locked;
        }

        if (IsExpired(now))
        {
            return OtpVerificationResult.Expired;
        }

        // Constant-time comparison: response time must not reveal how many characters matched.
        if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(attemptHash), Encoding.UTF8.GetBytes(CodeHash)))
        {
            Status = OtpChallengeStatus.Verified;
            VerifiedAt = now;
            return OtpVerificationResult.Success;
        }

        FailedAttempts++;
        if (FailedAttempts >= MaxAttempts)
        {
            Status = OtpChallengeStatus.Locked;
            return OtpVerificationResult.Locked;
        }

        return OtpVerificationResult.Invalid;
    }
}
