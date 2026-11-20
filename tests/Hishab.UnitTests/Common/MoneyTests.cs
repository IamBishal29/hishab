using Hishab.Domain.Common;

namespace Hishab.UnitTests.Common;

public class MoneyTests
{
    [Fact]
    public void Decimal_arithmetic_is_exact_where_double_drifts()
    {
        (0.1 + 0.2 == 0.3).ShouldBeFalse(); // why money is never double

        (Money.Bdt(0.1m) + Money.Bdt(0.2m)).ShouldBe(Money.Bdt(0.3m));
    }

    [Theory]
    [InlineData("60")]
    [InlineData("12.5")]
    [InlineData("12.50")]
    [InlineData("-40.25")]
    public void Accepts_up_to_two_decimal_places(string amount)
    {
        Money.Bdt(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)).Amount.ShouldBe(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Rejects_more_than_two_decimal_places_instead_of_rounding()
    {
        var error = Should.Throw<DomainValidationException>(() => Money.Bdt(12.505m, "price"));

        error.Errors.Keys.ShouldBe(["price"]);
    }

    [Fact]
    public void Equal_amounts_with_different_scale_are_equal()
    {
        Money.Bdt(12.5m).ShouldBe(Money.Bdt(12.50m));
    }

    [Fact]
    public void Subtraction_can_go_negative()
    {
        (Money.Bdt(50m) - Money.Bdt(60m)).IsNegative.ShouldBeTrue();
    }

    [Fact]
    public void Formats_with_currency()
    {
        Money.Bdt(1234.5m).ToString().ShouldBe("1234.50 BDT");
    }
}
