using Hishab.Domain.Common;
using Hishab.Domain.Shops;

namespace Hishab.UnitTests.Shops;

public class OtpChallengeTests
{
    private const string RightHash = "hash-of-123456";
    private const string WrongHash = "hash-of-000000";

    private static readonly DateTimeOffset _issuedAt = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private static OtpChallenge NewChallenge() => OtpChallenge.Issue(Phone.Parse("01712345678"), RightHash, _issuedAt);

    [Fact]
    public void A_new_challenge_is_pending_and_expires_after_five_minutes()
    {
        var challenge = NewChallenge();

        challenge.Status.ShouldBe(OtpChallengeStatus.Pending);
        challenge.ExpiresAt.ShouldBe(_issuedAt.AddMinutes(5));
        challenge.FailedAttempts.ShouldBe(0);
    }

    [Fact]
    public void The_right_code_within_the_lifetime_succeeds_once()
    {
        var challenge = NewChallenge();
        var at = _issuedAt.AddMinutes(4).AddSeconds(59);

        challenge.Verify(RightHash, at).ShouldBe(OtpVerificationResult.Success);

        challenge.Status.ShouldBe(OtpChallengeStatus.Verified);
        challenge.VerifiedAt.ShouldBe(at);
    }

    [Fact]
    public void A_used_code_cannot_be_replayed()
    {
        var challenge = NewChallenge();
        challenge.Verify(RightHash, _issuedAt.AddMinutes(1));

        challenge.Verify(RightHash, _issuedAt.AddMinutes(2)).ShouldBe(OtpVerificationResult.AlreadyUsed);
    }

    [Fact]
    public void The_code_is_expired_exactly_at_five_minutes()
    {
        var challenge = NewChallenge();

        challenge.Verify(RightHash, _issuedAt.AddMinutes(5)).ShouldBe(OtpVerificationResult.Expired);
        challenge.Status.ShouldBe(OtpChallengeStatus.Pending);
    }

    [Fact]
    public void A_wrong_code_counts_an_attempt()
    {
        var challenge = NewChallenge();

        challenge.Verify(WrongHash, _issuedAt.AddMinutes(1)).ShouldBe(OtpVerificationResult.Invalid);

        challenge.FailedAttempts.ShouldBe(1);
        challenge.Status.ShouldBe(OtpChallengeStatus.Pending);
    }

    [Fact]
    public void The_fifth_wrong_code_locks_the_challenge()
    {
        var challenge = NewChallenge();
        for (var i = 1; i < OtpChallenge.MaxAttempts; i++)
        {
            challenge.Verify(WrongHash, _issuedAt.AddMinutes(1)).ShouldBe(OtpVerificationResult.Invalid);
        }

        challenge.Verify(WrongHash, _issuedAt.AddMinutes(1)).ShouldBe(OtpVerificationResult.Locked);

        challenge.Status.ShouldBe(OtpChallengeStatus.Locked);
        challenge.FailedAttempts.ShouldBe(OtpChallenge.MaxAttempts);
    }

    [Fact]
    public void The_right_code_after_lockout_is_still_refused()
    {
        var challenge = NewChallenge();
        for (var i = 0; i < OtpChallenge.MaxAttempts; i++)
        {
            challenge.Verify(WrongHash, _issuedAt.AddMinutes(1));
        }

        challenge.Verify(RightHash, _issuedAt.AddMinutes(2)).ShouldBe(OtpVerificationResult.Locked);
        challenge.Status.ShouldBe(OtpChallengeStatus.Locked);
    }

    [Fact]
    public void An_expired_check_does_not_count_as_a_failed_attempt()
    {
        var challenge = NewChallenge();

        challenge.Verify(WrongHash, _issuedAt.AddMinutes(6));

        challenge.FailedAttempts.ShouldBe(0);
    }
}
