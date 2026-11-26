using System.Text.Json;
using Hishab.IntegrationTests.Finance;
using Hishab.IntegrationTests.Support;

namespace Hishab.IntegrationTests.Customers;

/// <summary>The risk badge on lookup, customer page and order details, from real delivered and returned parcels.</summary>
public class CustomerRiskApiTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    // Every FinanceScenario order goes to the same buyer.
    private const string BuyerPhone = "01912345678";

    private readonly FinanceScenario _shop = new(factory);

    private Task<JsonElement> Lookup(JsonElement owner) =>
        _shop.Ok(HttpMethod.Post, "/api/customers/lookup", owner, new { phone = BuyerPhone });

    /// <summary>"Level: Reason, Reason" (readable in failures, compared by value).</summary>
    private static string Risk(JsonElement risk) =>
        $"{risk.GetProperty("level").GetString()}: {string.Join(", ", risk.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()))}";

    private static string Level(JsonElement risk) => risk.GetProperty("level").GetString()!;

    [Fact]
    public async Task A_buyer_who_returns_parcels_is_Risky_everywhere_the_seller_looks()
    {
        var owner = await _shop.ShopWithFake();
        await _shop.DeliveredOrder(owner, price: 500m);
        await _shop.ReturnedOrder(owner, price: 500m);
        await _shop.ReturnedOrder(owner, price: 500m);
        var next = await _shop.ConfirmedOrder(owner, price: 500m, advance: 0m);

        var lookup = await Lookup(owner);
        Risk(lookup.GetProperty("risk")).ShouldBe("Risky: HighReturnRate, RecentReturn");
        lookup.GetProperty("risk").GetProperty("returnRate").GetDecimal().ShouldBe(0.67m);
        lookup.GetProperty("history").GetProperty("lastReturnedAt").ValueKind.ShouldBe(JsonValueKind.String);

        var customerId = lookup.GetProperty("customer").GetProperty("id").GetGuid();
        var page = await _shop.Ok(HttpMethod.Get, $"/api/customers/{customerId}", owner);
        Level(page.GetProperty("risk")).ShouldBe("Risky");

        var order = await _shop.Ok(HttpMethod.Get, $"/api/orders/{next}", owner);
        Level(order.GetProperty("customer").GetProperty("risk")).ShouldBe("Risky");
    }

    [Fact]
    public async Task A_first_order_is_New_and_does_not_count_against_itself()
    {
        var owner = await _shop.ShopWithFake();
        var orderId = await _shop.ReturnedOrder(owner, price: 500m);

        // The order on screen is left out of its own customer's record.
        var order = await _shop.Ok(HttpMethod.Get, $"/api/orders/{orderId}", owner);
        Risk(order.GetProperty("customer").GetProperty("risk")).ShouldBe("New: NoHistory");

        // Before the next order, the lookup counts that return.
        Level((await Lookup(owner)).GetProperty("risk")).ShouldBe("Watch");
    }

    [Fact]
    public async Task A_blocked_buyer_is_Risky_even_with_a_clean_record()
    {
        var owner = await _shop.ShopWithFake();
        await _shop.DeliveredOrder(owner, price: 500m);
        var customerId = (await Lookup(owner)).GetProperty("customer").GetProperty("id").GetGuid();

        await _shop.Ok(HttpMethod.Post, $"/api/customers/{customerId}/block", owner, new { reason = "Fake orders" });

        Risk((await Lookup(owner)).GetProperty("risk")).ShouldBe("Risky: Blocked");
    }

    [Fact]
    public async Task Another_shops_returns_do_not_follow_the_buyer()
    {
        var shopA = await _shop.ShopWithFake();
        await _shop.ReturnedOrder(shopA, price: 500m);
        await _shop.ReturnedOrder(shopA, price: 500m);
        var shopB = await _shop.ShopWithFake();
        await _shop.ConfirmedOrder(shopB, price: 500m, advance: 0m); // same phone, new to shop B

        Level((await Lookup(shopA)).GetProperty("risk")).ShouldBe("Risky");
        Risk((await Lookup(shopB)).GetProperty("risk")).ShouldBe("New: NoHistory");
    }

    [Fact]
    public async Task Each_order_list_row_carries_its_customers_risk_as_the_customer_page_shows_it()
    {
        var owner = await _shop.ShopWithFake();
        await _shop.DeliveredOrder(owner, price: 500m);
        await _shop.ReturnedOrder(owner, price: 500m);
        await _shop.ReturnedOrder(owner, price: 500m);
        await _shop.ConfirmedOrder(owner, price: 500m, advance: 0m, phone: "01812345678"); // a new buyer
        var buyer = (await Lookup(owner)).GetProperty("customer").GetProperty("id").GetGuid();
        var page = await _shop.Ok(HttpMethod.Get, $"/api/customers/{buyer}", owner);

        var rows = (await _shop.Ok(HttpMethod.Get, "/api/orders", owner)).GetProperty("items").EnumerateArray().ToList();

        rows.Count.ShouldBe(4);
        var buyersRows = rows.Where(r => r.GetProperty("customerId").GetGuid() == buyer).ToList();
        buyersRows.Count.ShouldBe(3);
        buyersRows.ShouldAllBe(r => Risk(r.GetProperty("customerRisk")) == Risk(page.GetProperty("risk")));
        Risk(page.GetProperty("risk")).ShouldStartWith("Risky");
        Risk(rows.Single(r => r.GetProperty("customerId").GetGuid() != buyer).GetProperty("customerRisk"))
            .ShouldBe("New: NoHistory");
    }
}
