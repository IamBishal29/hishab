using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Shops;

namespace Hishab.UnitTests.Shops;

public class ShopTests
{
    private static readonly Shop _shop = Shop.Create(DateTimeOffset.UtcNow);

    [Fact]
    public void New_shop_starts_with_default_delivery_charges()
    {
        var shop = Shop.Create(DateTimeOffset.UtcNow);

        shop.DeliveryChargeInsideDhaka.ShouldBe(Money.Bdt(60m));
        shop.DeliveryChargeOutsideDhaka.ShouldBe(Money.Bdt(120m));
        shop.DefaultCourier.ShouldBeNull();
    }

    [Fact]
    public void UpdateProfile_trims_text_and_turns_blanks_into_null()
    {
        var shop = Shop.Create(DateTimeOffset.UtcNow);

        shop.UpdateProfile("  Rahim Fashion ", "  ", " House 12, Road 5, Mirpur ", CourierProvider.Steadfast, Money.Bdt(70m), Money.Bdt(130m));

        shop.Name.ShouldBe("Rahim Fashion");
        shop.LogoUrl.ShouldBeNull();
        shop.PickupAddress.ShouldBe("House 12, Road 5, Mirpur");
        shop.DefaultCourier.ShouldBe(CourierProvider.Steadfast);
        shop.DeliveryChargeOutsideDhaka.ShouldBe(Money.Bdt(130m));
    }

    [Fact]
    public void UpdateProfile_reports_every_broken_rule_by_field_and_changes_nothing()
    {
        var error = Should.Throw<DomainValidationException>(() =>
            _shop.UpdateProfile(" ", "ftp://logo.png", new string('x', 301), null, Money.Bdt(-1m), Money.Bdt(-1m)));

        error.Errors.Keys.ShouldBe(
            ["name", "logoUrl", "pickupAddress", "deliveryChargeInsideDhaka", "deliveryChargeOutsideDhaka"],
            ignoreOrder: true);
        _shop.Name.ShouldBe(Shop.DefaultName);
    }

    [Fact]
    public void Free_delivery_is_allowed()
    {
        var shop = Shop.Create(DateTimeOffset.UtcNow);

        shop.UpdateProfile("Free Ship BD", null, null, null, Money.Zero, Money.Zero);

        shop.DeliveryChargeInsideDhaka.ShouldBe(Money.Zero);
    }
}
