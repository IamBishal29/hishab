using Hishab.Domain.Common;

namespace Hishab.UnitTests.Shops;

public class PhoneTests
{
    [Theory]
    [InlineData("01712345678")]
    [InlineData("8801712345678")]
    [InlineData("+8801712345678")]
    [InlineData("+880 1712-345678")]
    [InlineData(" 017 1234 5678 ")]
    [InlineData("(017) 1234-5678")]
    public void Every_common_spelling_normalises_to_e164(string input)
    {
        Phone.Parse(input).Value.ShouldBe("+8801712345678");
    }

    [Theory]
    [InlineData("01000000123")]
    [InlineData("+880 10000 00123")]
    public void The_fictional_demo_block_is_accepted_and_marked(string input)
    {
        var phone = Phone.Parse(input);

        phone.Value.ShouldBe("+8801000000123");
        phone.IsDemo.ShouldBeTrue();
        Phone.Parse("01712345678").IsDemo.ShouldBeFalse();
    }

    [Fact]
    public void Demo_numbers_are_built_from_an_index_inside_the_block()
    {
        Phone.Demo(7).Value.ShouldBe("+8801000000007");
        Phone.Demo(999).IsDemo.ShouldBeTrue();
        Should.Throw<ArgumentOutOfRangeException>(() => Phone.Demo(1000));
    }

    [Fact]
    public void Two_spellings_of_the_same_number_are_equal()
    {
        Phone.Parse("01712-345678").ShouldBe(Phone.Parse("+880 1712 345678"));
    }

    [Theory]
    [InlineData("01312345678")]
    [InlineData("01912345678")]
    public void Operator_prefixes_013_to_019_are_accepted(string input)
    {
        Phone.TryParse(input, out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0171234567")] // 10 digits
    [InlineData("017123456789")] // 12 digits
    [InlineData("01212345678")] // 012 is not a mobile operator
    [InlineData("01012345678")] // 010 outside the demo block
    [InlineData("01000001234")] // one digit past the demo block's prefix
    [InlineData("02912345678")] // landline
    [InlineData("4401712345678")] // other country code
    [InlineData("0171234567a")]
    [InlineData("0171+2345678")] // '+' only as a prefix
    public void Invalid_input_is_rejected(string? input)
    {
        Phone.TryParse(input, out var phone).ShouldBeFalse();
        phone.ShouldBeNull();
    }

    [Fact]
    public void Parse_throws_a_validation_error_without_echoing_the_input()
    {
        var ex = Should.Throw<DomainValidationException>(() => Phone.Parse("02912345678"));

        ex.Errors["phone"].ShouldBe([Phone.InvalidMessage]);
        ex.Message.ShouldNotContain("02912345678");
    }

    [Fact]
    public void ToString_is_masked_so_a_phone_cannot_leak_into_logs()
    {
        var phone = Phone.Parse("01712345678");

        phone.ToString().ShouldBe("01712****78");
        $"{phone}".ShouldNotContain("345");
    }
}
