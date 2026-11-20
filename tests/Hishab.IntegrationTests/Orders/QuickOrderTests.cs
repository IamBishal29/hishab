using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Api.Controllers;
using Hishab.Application.Common.Tenancy;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Persistence;
using Hishab.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.IntegrationTests.Orders;

public class QuickOrderTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    private static string BuyerPhone() => "019" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, JsonElement user, object? body = null, string? key = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = AuthSession.Bearer(user);
        if (key is not null)
        {
            request.Headers.Add(OrdersController.IdempotencyHeader, key);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request, Ct);
    }

    private Task<HttpResponseMessage> PostOrder(JsonElement user, object body, string? key = null) =>
        Send(HttpMethod.Post, "/api/orders", user, body, key ?? Guid.NewGuid().ToString());

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    /// <summary>An owner with a two-variant product (cost 400) and delivery charges 70 / 130.</summary>
    private async Task<(JsonElement Owner, string VariantM, string VariantL)> ShopWithCatalogue()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        (await Send(HttpMethod.Put, "/api/shop", owner, new { name = "Rahim Fashion", deliveryChargeInsideDhaka = 70m, deliveryChargeOutsideDhaka = 130m }))
            .EnsureSuccessStatusCode();
        var product = await Json(await Send(HttpMethod.Post, "/api/products", owner, new
        {
            name = "Polo shirt",
            variants = new object[]
            {
                new { sku = "POLO-M", size = "M", colour = "Red", price = 650m, costPrice = 400m },
                new { sku = "POLO-L", size = "L", price = 700m, costPrice = 420m },
            },
        }));
        var variants = product.GetProperty("variants");
        return (owner, variants[0].GetProperty("id").GetString()!, variants[1].GetProperty("id").GetString()!);
    }

    private static object OrderBody(string phone, string variantId, int quantity = 2, bool insideDhaka = true, decimal? delivery = null, decimal advance = 0m) => new
    {
        phone,
        customerName = "Rahima Begum",
        deliveryAddress = "House 4, Road 2, Mirpur",
        isInsideDhaka = insideDhaka,
        source = "Facebook",
        lines = new[] { new { variantId, quantity } },
        deliveryCharge = delivery,
        advancePaid = advance,
    };

    private async Task<int> OrdersInDb(JsonElement owner)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(owner.GetProperty("shopId").GetGuid());
        return await scope.ServiceProvider.GetRequiredService<HishabDbContext>().Orders.CountAsync(Ct);
    }

    private async Task<int> OrdersUsed(JsonElement owner) =>
        (await Json(await Send(HttpMethod.Get, "/api/shop/usage", owner))).GetProperty("orders").GetProperty("used").GetInt32();

    [Fact]
    public async Task Creates_an_order_from_the_catalogue_with_the_shops_delivery_charge()
    {
        var (owner, variantM, _) = await ShopWithCatalogue();

        var response = await PostOrder(owner, OrderBody(BuyerPhone(), variantM, quantity: 2, advance: 100m));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var details = await Json(response);
        var order = details.GetProperty("order");
        order.GetProperty("number").GetInt32().ShouldBe(1);
        order.GetProperty("status").GetString().ShouldBe("New");
        var line = order.GetProperty("lines")[0];
        line.GetProperty("productName").GetString().ShouldBe("Polo shirt");
        line.GetProperty("variantLabel").GetString().ShouldBe("M / Red");
        line.GetProperty("unitPrice").GetDecimal().ShouldBe(650m);
        line.GetProperty("unitCost").GetDecimal().ShouldBe(400m); // the owner sees cost
        order.GetProperty("deliveryCharge").GetDecimal().ShouldBe(70m); // inside Dhaka from settings
        order.GetProperty("total").GetDecimal().ShouldBe(1_370m);
        order.GetProperty("codAmount").GetDecimal().ShouldBe(1_270m);
        details.GetProperty("customer").GetProperty("previousOrders").GetInt32().ShouldBe(0);
        details.GetProperty("warnings").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Outside_Dhaka_rate_applies_and_an_override_wins()
    {
        var (owner, variantM, _) = await ShopWithCatalogue();

        var outside = await Json(await PostOrder(owner, OrderBody(BuyerPhone(), variantM, insideDhaka: false)));
        var free = await Json(await PostOrder(owner, OrderBody(BuyerPhone(), variantM, delivery: 0m)));

        outside.GetProperty("order").GetProperty("deliveryCharge").GetDecimal().ShouldBe(130m);
        free.GetProperty("order").GetProperty("deliveryCharge").GetDecimal().ShouldBe(0m);
    }

    [Fact]
    public async Task Returning_customer_is_matched_and_their_history_is_summarised()
    {
        var (owner, variantM, variantL) = await ShopWithCatalogue();
        var phone = BuyerPhone();
        var first = await Json(await PostOrder(owner, OrderBody(phone, variantM)));

        var second = await Json(await PostOrder(owner, OrderBody("+88" + phone, variantL)));

        second.GetProperty("customer").GetProperty("id").GetGuid().ShouldBe(first.GetProperty("customer").GetProperty("id").GetGuid());
        second.GetProperty("customer").GetProperty("previousOrders").GetInt32().ShouldBe(1);
        second.GetProperty("order").GetProperty("number").GetInt32().ShouldBe(2);

        // The quick-order form's lookup shows the same track record before the next order.
        (await Send(HttpMethod.Post, $"/api/orders/{first.GetProperty("order").GetProperty("id").GetGuid()}/cancel", owner)).EnsureSuccessStatusCode();
        var lookup = await Json(await Send(HttpMethod.Post, "/api/customers/lookup", owner, new { phone }));
        lookup.GetProperty("customer").GetProperty("name").GetString().ShouldBe("Rahima Begum");
        var history = lookup.GetProperty("history");
        history.GetProperty("previousOrders").GetInt32().ShouldBe(2);
        history.GetProperty("cancelled").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Blocked_customer_gives_a_warning_not_a_refusal()
    {
        var (owner, variantM, _) = await ShopWithCatalogue();
        var phone = BuyerPhone();
        var customer = await Json(await Send(HttpMethod.Post, "/api/customers", owner, new { phone }));
        await Send(HttpMethod.Post, $"/api/customers/{customer.GetProperty("id").GetGuid()}/block", owner, new { reason = "Refused 3 parcels" });

        var response = await PostOrder(owner, OrderBody(phone, variantM));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Json(response)).GetProperty("warnings")[0].GetString().ShouldBe("customerBlocked");
    }

    [Fact]
    public async Task Same_key_and_request_replays_the_first_order()
    {
        var (owner, variantM, _) = await ShopWithCatalogue();
        var key = Guid.NewGuid().ToString();
        var body = OrderBody(BuyerPhone(), variantM);

        var first = await PostOrder(owner, body, key);
        var retry = await PostOrder(owner, body, key);

        retry.StatusCode.ShouldBe(HttpStatusCode.Created);
        retry.Headers.GetValues(OrdersController.ReplayedHeader).Single().ShouldBe("true");
        first.Headers.Contains(OrdersController.ReplayedHeader).ShouldBeFalse();
        (await Json(retry)).GetProperty("order").GetProperty("id").GetGuid()
            .ShouldBe((await Json(first)).GetProperty("order").GetProperty("id").GetGuid());
        (await OrdersInDb(owner)).ShouldBe(1);
        (await OrdersUsed(owner)).ShouldBe(1);
    }

    [Fact]
    public async Task Same_key_with_a_different_request_is_refused()
    {
        var (owner, variantM, _) = await ShopWithCatalogue();
        var key = Guid.NewGuid().ToString();
        await PostOrder(owner, OrderBody(BuyerPhone(), variantM, quantity: 1), key);

        var reused = await PostOrder(owner, OrderBody(BuyerPhone(), variantM, quantity: 5), key);

        reused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await OrdersInDb(owner)).ShouldBe(1);
    }

    [Fact]
    public async Task Parallel_submits_with_one_key_create_one_order_and_use_quota_once()
    {
        var (owner, variantM, _) = await ShopWithCatalogue();
        var key = Guid.NewGuid().ToString();
        var body = OrderBody(BuyerPhone(), variantM);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PostOrder(owner, body, key)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        var ids = await Task.WhenAll(responses.Select(async r => (await Json(r)).GetProperty("order").GetProperty("id").GetGuid()));
        ids.Distinct().ShouldHaveSingleItem();
        (await OrdersInDb(owner)).ShouldBe(1);
        (await OrdersUsed(owner)).ShouldBe(1);

        var next = await Json(await PostOrder(owner, OrderBody(BuyerPhone(), variantM)));
        next.GetProperty("order").GetProperty("number").GetInt32().ShouldBe(2); // losers gave their numbers back
    }

    [Fact]
    public async Task Missing_idempotency_key_is_rejected()
    {
        var (owner, variantM, _) = await ShopWithCatalogue();

        var response = await Send(HttpMethod.Post, "/api/orders", owner, OrderBody(BuyerPhone(), variantM));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_deleted_or_foreign_products_are_rejected_without_using_quota()
    {
        var (owner, variantM, variantL) = await ShopWithCatalogue();
        var (_, foreignVariant, _) = await ShopWithCatalogue();
        var product = await Json(await Send(HttpMethod.Get, "/api/products", owner));
        await Send(HttpMethod.Delete, $"/api/products/{product.GetProperty("items")[0].GetProperty("id").GetGuid()}/variants/{variantL}", owner);

        foreach (var variant in new[] { Guid.NewGuid().ToString(), variantL, foreignVariant })
        {
            var response = await PostOrder(owner, OrderBody(BuyerPhone(), variant));
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await Json(response)).GetProperty("errors").TryGetProperty("lines[0].variantId", out _).ShouldBeTrue();
        }

        (await OrdersUsed(owner)).ShouldBe(0);
        (await Json(await PostOrder(owner, OrderBody(BuyerPhone(), variantM)))).GetProperty("order").GetProperty("number").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Plan_limit_stops_orders_and_rolls_back_the_customer_and_number()
    {
        var (owner, variantM, _) = await ShopWithCatalogue();
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(owner.GetProperty("shopId").GetGuid());
            var db = scope.ServiceProvider.GetRequiredService<HishabDbContext>();
            db.UsageCounters.Add(new UsageCounter(BillingPeriod.Containing(factory.Clock.GetUtcNow()).Key, UsageMetric.Orders, 100));
            await db.SaveChangesAsync(Ct);
        }

        var phone = BuyerPhone();
        var response = await PostOrder(owner, OrderBody(phone, variantM));

        response.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        (await Send(HttpMethod.Post, "/api/customers/lookup", owner, new { phone })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Moderator_creates_orders_without_seeing_costs_and_packer_cannot()
    {
        var (owner, variantM, _) = await ShopWithCatalogue();
        var moderator = await factory.InviteAndSignInAsync(_client, owner, "Moderator", Ct);
        var packer = await factory.InviteAndSignInAsync(_client, owner, "Packer", Ct);

        var created = await PostOrder(moderator, OrderBody(BuyerPhone(), variantM));

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await Json(created)).GetProperty("order").GetProperty("lines")[0].GetProperty("unitCost").ValueKind.ShouldBe(JsonValueKind.Null);
        (await PostOrder(packer, OrderBody(BuyerPhone(), variantM))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Orders_and_their_keys_belong_to_one_shop()
    {
        var (shopA, variantA, _) = await ShopWithCatalogue();
        var (shopB, variantB, _) = await ShopWithCatalogue();
        var key = Guid.NewGuid().ToString();

        var inA = await Json(await PostOrder(shopA, OrderBody(BuyerPhone(), variantA), key));
        var inB = await PostOrder(shopB, OrderBody(BuyerPhone(), variantB), key); // same key, other shop: independent

        inB.StatusCode.ShouldBe(HttpStatusCode.Created);
        inB.Headers.Contains(OrdersController.ReplayedHeader).ShouldBeFalse();
        (await Send(HttpMethod.Get, $"/api/orders/{inA.GetProperty("order").GetProperty("id").GetGuid()}", shopB))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
