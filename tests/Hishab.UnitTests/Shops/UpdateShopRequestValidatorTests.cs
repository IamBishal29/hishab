using Hishab.Api.Controllers;

namespace Hishab.UnitTests.Shops;

public class UpdateShopRequestValidatorTests
{
    private static readonly UpdateShopRequestValidator _validator = new();

    private static readonly UpdateShopRequest _valid = new()
    {
        Name = "Rahim Fashion",
        DeliveryChargeInsideDhaka = 60m,
        DeliveryChargeOutsideDhaka = 120m,
    };

    [Fact]
    public void Minimal_request_is_valid()
    {
        _validator.Validate(_valid).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("60.001")]
    [InlineData("5000.01")]
    public void Rejects_negative_too_precise_or_implausible_charges(string charge)
    {
        var request = new UpdateShopRequest { Name = "Shop", DeliveryChargeInsideDhaka = decimal.Parse(charge, System.Globalization.CultureInfo.InvariantCulture), DeliveryChargeOutsideDhaka = 120m };

        _validator.Validate(request).Errors.Single().PropertyName.ShouldBe(nameof(UpdateShopRequest.DeliveryChargeInsideDhaka));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    [InlineData("/relative/logo.png")]
    public void Rejects_non_http_logo_urls(string logoUrl)
    {
        var request = new UpdateShopRequest { Name = "Shop", LogoUrl = logoUrl, DeliveryChargeInsideDhaka = 60m, DeliveryChargeOutsideDhaka = 120m };

        _validator.Validate(request).Errors.Single().PropertyName.ShouldBe(nameof(UpdateShopRequest.LogoUrl));
    }

    [Fact]
    public void Rejects_an_unknown_courier_number()
    {
        var request = new UpdateShopRequest { Name = "Shop", DefaultCourier = (Domain.Couriers.CourierProvider)99, DeliveryChargeInsideDhaka = 60m, DeliveryChargeOutsideDhaka = 120m };

        _validator.Validate(request).IsValid.ShouldBeFalse();
    }
}
