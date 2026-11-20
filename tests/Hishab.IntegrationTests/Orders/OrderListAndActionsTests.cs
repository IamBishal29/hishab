using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Api.Controllers;
using Hishab.Application.Common.Tenancy;
using Hishab.Infrastructure.Persistence;
using Hishab.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.IntegrationTests.Orders;

public class OrderListAndActionsTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    private static string BuyerPhone() => "019" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, JsonElement user, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = AuthSession.Bearer(user);
        if (method == HttpMethod.Post && path == "/api/orders")
        {
            request.Headers.Add(OrdersController.IdempotencyHeader, Guid.NewGuid().ToString());
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    private async Task<(JsonElement Owner, string VariantId)> ShopWithProduct()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var product = await Json(await Send(HttpMethod.Post, "/api/products", owner, new
        {
            name = "Polo shirt",
            variants = new[] { new { sku = "POLO-M", price = 650m } },
        }));
        return (owner, product.GetProperty("variants")[0].GetProperty("id").GetString()!);
    }

    private async Task<JsonElement> CreateOrder(
        JsonElement user, string variantId, string? phone = null, string name = "Rahima Begum", bool insideDhaka = true, string source = "Facebook")
    {
        var response = await Send(HttpMethod.Post, "/api/orders", user, new
        {
            phone = phone ?? BuyerPhone(),
            customerName = name,
            isInsideDhaka = insideDhaka,
            source,
            lines = new[] { new { variantId, quantity = 1 } },
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await Json(response)).GetProperty("order");
    }

    private async Task<JsonElement> List(JsonElement user, string query = "")
    {
        var response = await Send(HttpMethod.Get, "/api/orders" + query, user);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await Json(response);
    }

    private static int[] Numbers(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("number").GetInt32())];

    private async Task SetCreatedAt(JsonElement owner, JsonElement order, DateTimeOffset createdAt)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(owner.GetProperty("shopId").GetGuid());
        var db = scope.ServiceProvider.GetRequiredService<HishabDbContext>();
        var id = order.GetProperty("id").GetGuid();
        await db.Database.ExecuteSqlAsync($"UPDATE orders SET created_at = {createdAt} WHERE id = {id}", Ct);
    }

    [Fact]
    public async Task Filters_by_status_area_source_and_text()
    {
        var (owner, variant) = await ShopWithProduct();
        var phone = BuyerPhone();
        var o1 = await CreateOrder(owner, variant, phone: phone, name: "Karim Uddin");
        await CreateOrder(owner, variant, insideDhaka: false, source: "Instagram");
        var o3 = await CreateOrder(owner, variant, source: "WhatsApp");
        (await Send(HttpMethod.Post, $"/api/orders/{o3.GetProperty("id").GetGuid()}/confirm", owner)).StatusCode.ShouldBe(HttpStatusCode.OK);

        Numbers(await List(owner)).ShouldBe([3, 2, 1]); // newest first
        Numbers(await List(owner, "?status=Confirmed")).ShouldBe([3]);
        Numbers(await List(owner, "?status=New&status=Confirmed")).ShouldBe([3, 2, 1]);
        Numbers(await List(owner, "?isInsideDhaka=false")).ShouldBe([2]);
        Numbers(await List(owner, "?source=Instagram")).ShouldBe([2]);
        Numbers(await List(owner, "?search=%231")).ShouldBe([1]); // "#1"
        Numbers(await List(owner, "?search=karim")).ShouldBe([1]);
        Numbers(await List(owner, "?search=" + phone[^6..])).ShouldBe([1]);

        var row = (await List(owner, "?search=karim")).GetProperty("items")[0];
        row.GetProperty("id").GetGuid().ShouldBe(o1.GetProperty("id").GetGuid());
        row.GetProperty("customerName").GetString().ShouldBe("Karim Uddin");
        row.GetProperty("itemCount").GetInt32().ShouldBe(1);
        row.GetProperty("total").GetDecimal().ShouldBe(o1.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Date_range_uses_Dhaka_calendar_days()
    {
        var (owner, variant) = await ShopWithProduct();
        var lateOn19th = await CreateOrder(owner, variant); // 23:30 Dhaka on the 19th
        var earlyOn20th = await CreateOrder(owner, variant); // 00:30 Dhaka on the 20th (still the 19th in UTC)
        await SetCreatedAt(owner, lateOn19th, new DateTimeOffset(2026, 11, 19, 17, 30, 0, TimeSpan.Zero));
        await SetCreatedAt(owner, earlyOn20th, new DateTimeOffset(2026, 11, 19, 18, 30, 0, TimeSpan.Zero));

        Numbers(await List(owner, "?from=2026-11-20&to=2026-11-20")).ShouldBe([2]);
        Numbers(await List(owner, "?from=2026-11-19&to=2026-11-19")).ShouldBe([1]);
        Numbers(await List(owner, "?to=2026-11-20")).ShouldBe([2, 1]);
    }

    [Fact]
    public async Task Keyset_pages_never_skip_or_repeat_even_when_new_orders_arrive()
    {
        var (owner, variant) = await ShopWithProduct();
        for (var i = 0; i < 5; i++)
        {
            await CreateOrder(owner, variant);
        }

        var first = await List(owner, "?limit=2");
        await CreateOrder(owner, variant); // #6 arrives while the seller scrolls: with OFFSET, #3 would show twice
        var second = await List(owner, "?limit=2&cursor=" + first.GetProperty("nextCursor").GetString());
        var third = await List(owner, "?limit=2&cursor=" + second.GetProperty("nextCursor").GetString());

        Numbers(first).ShouldBe([5, 4]);
        Numbers(second).ShouldBe([3, 2]);
        Numbers(third).ShouldBe([1]);
        third.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Invalid_cursor_or_range_is_a_400()
    {
        var (owner, _) = await ShopWithProduct();

        var badCursor = await Send(HttpMethod.Get, "/api/orders?cursor=forged", owner);
        var badRange = await Send(HttpMethod.Get, "/api/orders?from=2026-11-20&to=2026-11-01", owner);

        badCursor.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(badCursor)).GetProperty("errors").TryGetProperty("cursor", out _).ShouldBeTrue();
        badRange.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Confirm_then_cancel_with_a_reason_and_wrong_moves_are_409()
    {
        var (owner, variant) = await ShopWithProduct();
        var id = (await CreateOrder(owner, variant)).GetProperty("id").GetGuid();

        var confirmed = await Send(HttpMethod.Post, $"/api/orders/{id}/confirm", owner);
        var again = await Send(HttpMethod.Post, $"/api/orders/{id}/confirm", owner);
        var cancelled = await Send(HttpMethod.Post, $"/api/orders/{id}/cancel", owner, new { note = "Buyer changed mind" });
        var missing = await Send(HttpMethod.Post, $"/api/orders/{Guid.NewGuid()}/confirm", owner);

        confirmed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(confirmed)).GetProperty("order").GetProperty("status").GetString().ShouldBe("Confirmed");
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var history = (await Json(cancelled)).GetProperty("order").GetProperty("history");
        history.GetArrayLength().ShouldBe(3);
        history[2].GetProperty("to").GetString().ShouldBe("Cancelled");
        history[2].GetProperty("note").GetString().ShouldBe("Buyer changed mind");
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Bulk_confirm_reports_each_order_and_keeps_the_ones_that_succeeded()
    {
        var (owner, variant) = await ShopWithProduct();
        var ok1 = (await CreateOrder(owner, variant)).GetProperty("id").GetGuid();
        var ok2 = (await CreateOrder(owner, variant)).GetProperty("id").GetGuid();
        var cancelled = (await CreateOrder(owner, variant)).GetProperty("id").GetGuid();
        (await Send(HttpMethod.Post, $"/api/orders/{cancelled}/cancel", owner)).EnsureSuccessStatusCode();
        var unknown = Guid.NewGuid();

        var response = await Send(HttpMethod.Post, "/api/orders/bulk/confirm", owner, new { orderIds = new[] { ok1, cancelled, unknown, ok2 } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await Json(response);
        body.GetProperty("succeeded").GetInt32().ShouldBe(2);
        body.GetProperty("failed").GetInt32().ShouldBe(2);
        var results = body.GetProperty("results").EnumerateArray().ToDictionary(r => r.GetProperty("orderId").GetGuid());
        results[ok1].GetProperty("succeeded").GetBoolean().ShouldBeTrue();
        results[ok2].GetProperty("status").GetString().ShouldBe("Confirmed");
        results[cancelled].GetProperty("error").GetString().ShouldBe("invalidTransition");
        results[cancelled].GetProperty("status").GetString().ShouldBe("Cancelled");
        results[unknown].GetProperty("error").GetString().ShouldBe("notFound");
        Numbers(await List(owner, "?status=Confirmed")).Length.ShouldBe(2); // the failures did not undo the successes
    }

    [Fact]
    public async Task Bulk_request_is_validated()
    {
        var (owner, _) = await ShopWithProduct();

        var empty = await Send(HttpMethod.Post, "/api/orders/bulk/cancel", owner, new { orderIds = Array.Empty<Guid>() });
        var tooMany = await Send(HttpMethod.Post, "/api/orders/bulk/cancel", owner, new { orderIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()) });

        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        tooMany.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Parallel_confirms_of_one_order_succeed_once_and_the_rest_get_409()
    {
        var (owner, variant) = await ShopWithProduct();
        var id = (await CreateOrder(owner, variant)).GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Send(HttpMethod.Post, $"/api/orders/{id}/confirm", owner)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(4); // lost the race or no longer New
        var order = await Json(await Send(HttpMethod.Get, $"/api/orders/{id}", owner));
        order.GetProperty("order").GetProperty("history").GetArrayLength().ShouldBe(2); // created + one confirm
    }

    [Fact]
    public async Task Packers_read_the_list_but_cannot_confirm_or_cancel()
    {
        var (owner, variant) = await ShopWithProduct();
        var id = (await CreateOrder(owner, variant)).GetProperty("id").GetGuid();
        var packer = await factory.InviteAndSignInAsync(_client, owner, "Packer", Ct);

        Numbers(await List(packer)).ShouldBe([1]);
        (await Send(HttpMethod.Get, $"/api/orders/{id}", packer)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Send(HttpMethod.Post, $"/api/orders/{id}/confirm", packer)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Post, "/api/orders/bulk/cancel", packer, new { orderIds = new[] { id } })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Another_shops_orders_are_invisible_to_list_and_bulk_actions()
    {
        var (shopA, variantA) = await ShopWithProduct();
        var (shopB, _) = await ShopWithProduct();
        var orderA = (await CreateOrder(shopA, variantA)).GetProperty("id").GetGuid();

        var listB = await List(shopB);
        var bulkB = await Json(await Send(HttpMethod.Post, "/api/orders/bulk/cancel", shopB, new { orderIds = new[] { orderA } }));

        listB.GetProperty("items").GetArrayLength().ShouldBe(0);
        bulkB.GetProperty("results")[0].GetProperty("error").GetString().ShouldBe("notFound");
        (await Send(HttpMethod.Post, $"/api/orders/{orderA}/cancel", shopB)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Numbers(await List(shopA, "?status=New")).ShouldBe([1]); // untouched
    }
}
