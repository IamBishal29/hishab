using Hishab.Application.Shops.Auth;

namespace Hishab.UnitTests.Shops;

public class OtpCodeTests
{
    [Fact]
    public void Codes_are_six_digits_including_leading_zeros()
    {
        var codes = Enumerable.Range(0, 2_000).Select(_ => OtpCode.Generate()).ToList();

        codes.ShouldAllBe(c => c.Length == OtpCode.Length && c.All(char.IsAsciiDigit));
        codes.ShouldContain(c => c.StartsWith('0'), "zero-padding must keep codes below 100000 at six digits");
    }

    [Fact]
    public void Codes_are_not_repeated_in_a_small_sample()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => OtpCode.Generate()).ToList();

        // 200 draws from a million: a collision is possible but very unlikely (~2%); many would mean a broken RNG.
        codes.Distinct().Count().ShouldBeGreaterThanOrEqualTo(198);
    }
}
