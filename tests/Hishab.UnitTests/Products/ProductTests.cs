using Hishab.Domain.Common;
using Hishab.Domain.Products;

namespace Hishab.UnitTests.Products;

public class ProductTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private static VariantDetails Variant(string sku, decimal price = 650m, decimal? cost = 400m, string? size = "M", string? colour = "Red") =>
        new(sku, size, colour, Money.Bdt(price), cost is { } c ? Money.Bdt(c) : null);

    private static Product Polo() =>
        Product.Create("Polo shirt", null, [Variant("polo-m-red"), Variant("POLO-L-BLU", size: "L", colour: "Blue")], _now);

    [Fact]
    public void Skus_are_trimmed_and_upper_case()
    {
        Polo().Variants.Select(v => v.Sku).ShouldBe(["POLO-M-RED", "POLO-L-BLU"]);
    }

    [Fact]
    public void A_product_needs_at_least_one_variant()
    {
        Should.Throw<DomainValidationException>(() => Product.Create("Saree", null, [], _now))
            .Errors.Keys.ShouldBe(["variants"]);
    }

    [Fact]
    public void A_simple_product_is_one_variant_without_options()
    {
        var saree = Product.Create("Jamdani saree", null, [Variant("SAREE-1", size: null, colour: null)], _now);

        saree.Variants.ShouldHaveSingleItem().Size.ShouldBeNull();
    }

    [Fact]
    public void Duplicate_sku_within_a_product_is_rejected_case_insensitively()
    {
        var error = Should.Throw<DomainValidationException>(() =>
            Product.Create("Polo", null, [Variant("POLO-1"), Variant(" polo-1 ")], _now));

        error.Errors.Keys.ShouldBe(["variants[1].sku"]);
    }

    [Theory]
    [InlineData(-1, 400, "variants[0].price")]
    [InlineData(650, -1, "variants[0].costPrice")]
    public void Negative_prices_are_rejected(decimal price, decimal cost, string field)
    {
        Should.Throw<DomainValidationException>(() => Product.Create("Polo", null, [Variant("P1", price, cost)], _now))
            .Errors.Keys.ShouldBe([field]);
    }

    [Fact]
    public void Margin_is_price_minus_cost_and_may_be_negative()
    {
        var product = Product.Create("Clearance tee", null, [Variant("TEE", price: 300m, cost: 350m)], _now);

        product.Variants[0].Margin.ShouldBe(Money.Bdt(-50m));
    }

    [Fact]
    public void Margin_is_unknown_without_a_cost()
    {
        Product.Create("Tee", null, [Variant("TEE", cost: null)], _now).Variants[0].Margin.ShouldBeNull();
    }

    [Fact]
    public void Updating_without_a_cost_keeps_the_existing_cost()
    {
        var polo = Polo();
        var variant = polo.Variants[0];

        polo.UpdateVariant(variant.Id, Variant("POLO-M-RED", price: 700m, cost: null));

        variant.Price.ShouldBe(Money.Bdt(700m));
        variant.CostPrice.ShouldBe(Money.Bdt(400m));
    }

    [Fact]
    public void Renaming_a_variant_to_a_sibling_sku_is_rejected()
    {
        var polo = Polo();

        Should.Throw<DomainValidationException>(() => polo.UpdateVariant(polo.Variants[0].Id, Variant("polo-l-blu")))
            .Errors.Keys.ShouldBe(["sku"]);
    }

    [Fact]
    public void The_last_active_variant_cannot_be_removed()
    {
        var polo = Polo();
        polo.RemoveVariant(polo.Variants[0].Id, _now);

        Should.Throw<ConflictException>(() => polo.RemoveVariant(polo.Variants[1].Id, _now));
        polo.ActiveVariants.ShouldHaveSingleItem();
    }

    [Fact]
    public void A_removed_variants_sku_can_be_reused()
    {
        var polo = Polo();
        polo.RemoveVariant(polo.Variants[0].Id, _now);

        polo.AddVariant(Variant("POLO-M-RED")).Sku.ShouldBe("POLO-M-RED");
    }

    [Fact]
    public void Deleting_a_product_soft_deletes_it_and_its_variants_and_freezes_it()
    {
        var polo = Polo();

        polo.Delete(_now);

        polo.DeletedAt.ShouldBe(_now);
        polo.Variants.ShouldAllBe(v => v.DeletedAt == _now);
        Should.Throw<ConflictException>(() => polo.Rename("New name", null));
    }
}
