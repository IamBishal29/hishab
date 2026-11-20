using System.Security.Cryptography;
using Hishab.Domain.Common;
using Hishab.Infrastructure.Shops.Auth;
using Microsoft.Extensions.Options;

namespace Hishab.UnitTests.Shops;

public class HmacOtpHasherTests
{
    private static readonly Phone _phone = Phone.Parse("01712345678");

    private static HmacOtpHasher Hasher(string? key = null) =>
        new(Options.Create(new OtpOptions { HashKey = key ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    [Fact]
    public void The_same_phone_and_code_give_the_same_hash()
    {
        var hasher = Hasher();

        hasher.Hash(_phone, "123456").ShouldBe(hasher.Hash(_phone, "123456"));
    }

    [Fact]
    public void The_hash_is_bound_to_the_phone()
    {
        var hasher = Hasher();

        hasher.Hash(_phone, "123456").ShouldNotBe(hasher.Hash(Phone.Parse("01812345678"), "123456"));
    }

    [Fact]
    public void A_different_key_gives_a_different_hash_so_a_leaked_table_is_useless_alone()
    {
        Hasher().Hash(_phone, "123456").ShouldNotBe(Hasher().Hash(_phone, "123456"));
    }

    [Fact]
    public void The_hash_is_64_hex_characters_and_does_not_contain_the_code()
    {
        var hash = Hasher().Hash(_phone, "123456");

        hash.ShouldMatch("^[0-9A-F]{64}$");
        hash.ShouldNotContain("123456");
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("c2hvcnQ=", false)] // "short": 5 bytes
    [InlineData("not base64!", false)]
    public void Keys_shorter_than_32_bytes_or_not_base64_are_rejected(string key, bool valid)
    {
        new OtpOptions { HashKey = key }.HasValidKey().ShouldBe(valid);
    }

    [Fact]
    public void A_32_byte_base64_key_is_accepted()
    {
        new OtpOptions { HashKey = Convert.ToBase64String(new byte[32]) }.HasValidKey().ShouldBeTrue();
    }
}
