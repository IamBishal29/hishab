using Hishab.Domain.Shops;

namespace Hishab.UnitTests.Shops;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private static RefreshToken NewToken() =>
        RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "HASH", _now, TimeSpan.FromDays(30));

    [Fact]
    public void Issue_sets_expiry_from_the_lifetime()
    {
        NewToken().ExpiresAt.ShouldBe(_now.AddDays(30));
    }

    [Fact]
    public void Redeem_succeeds_once_and_records_the_time()
    {
        var token = NewToken();

        token.Redeem(_now.AddMinutes(20)).ShouldBe(RefreshTokenRedemption.Success);
        token.UsedAt.ShouldBe(_now.AddMinutes(20));
    }

    [Fact]
    public void Redeeming_again_is_reported_as_reuse()
    {
        var token = NewToken();
        token.Redeem(_now);

        token.Redeem(_now.AddMinutes(1)).ShouldBe(RefreshTokenRedemption.Reused);
    }

    [Fact]
    public void Reuse_is_reported_even_after_the_token_expired()
    {
        var token = NewToken();
        token.Redeem(_now);

        token.Redeem(_now.AddDays(31)).ShouldBe(RefreshTokenRedemption.Reused);
    }

    [Fact]
    public void Redeem_fails_at_the_expiry_instant()
    {
        var token = NewToken();

        token.Redeem(_now.AddDays(30)).ShouldBe(RefreshTokenRedemption.Expired);
        token.UsedAt.ShouldBeNull();
    }

    [Fact]
    public void Issue_rejects_a_non_positive_lifetime()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            RefreshToken.Issue(Guid.NewGuid(), Guid.NewGuid(), "HASH", _now, TimeSpan.Zero));
    }
}
