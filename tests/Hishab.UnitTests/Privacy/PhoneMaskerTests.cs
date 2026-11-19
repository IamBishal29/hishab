using Hishab.Application.Common.Privacy;

namespace Hishab.UnitTests.Privacy;

public class PhoneMaskerTests
{
    [Theory]
    [InlineData("01712345678", "01712****78")]
    [InlineData("+8801712345678", "01712****78")]
    [InlineData("8801712345678", "01712****78")]
    [InlineData("+880 1712-345678", "01712****78")]
    [InlineData("017 1234 5678", "01712****78")]
    public void Mask_hides_the_middle_of_a_bangladeshi_mobile_number(string phone, string expected)
    {
        PhoneMasker.Mask(phone).ShouldBe(expected);
    }

    [Theory]
    [InlineData("12345", "****45")]
    [InlineData("7", "****")]
    public void Mask_keeps_only_the_last_two_digits_of_unrecognised_input(string phone, string expected)
    {
        PhoneMasker.Mask(phone).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Mask_returns_empty_for_missing_input(string? phone)
    {
        PhoneMasker.Mask(phone).ShouldBeEmpty();
    }

    [Fact]
    public void MaskAll_masks_every_phone_number_inside_free_text()
    {
        var text = "Call 01712345678 or +880 1819-876543 before delivery";

        PhoneMasker.MaskAll(text).ShouldBe("Call 01712****78 or 01819****43 before delivery");
    }

    [Theory]
    [InlineData("Order 12345678901 total 1500")] // 11 digits but not a mobile prefix
    [InlineData("Consignment 017123456789012")] // longer digit run, not a phone
    [InlineData("Paid 250 BDT")]
    public void Text_without_a_phone_number_is_left_unchanged(string text)
    {
        PhoneMasker.ContainsPhone(text).ShouldBeFalse();
        PhoneMasker.MaskAll(text).ShouldBe(text);
    }
}
