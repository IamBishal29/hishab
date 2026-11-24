using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Api.Controllers;
using Hishab.Application.Common.Tenancy;
using Hishab.Domain.Couriers;
using Hishab.Domain.Orders;
using Hishab.Infrastructure.Couriers.Fake;
using Hishab.Infrastructure.Jobs;
using Hishab.Infrastructure.Persistence;
using Hishab.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.IntegrationTests.Finance;

/// <summary>GET /api/money/cod-summary over real orders, delivered through the status sync job.</summary>
public class CodSummaryTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, JsonElement user, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = AuthSession.Bearer(user);
        if (path == "/api/orders")
        {
            request.Headers.Add(OrdersController.IdempotencyHeader, Guid.NewGuid().ToString());
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request, Ct);
    }

    private async Task<JsonElement> Ok(HttpMethod method, string path, JsonElement user, object? body = null)
    {
        var response = await Send(method, path, user, body);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }

    private static Guid ShopId(JsonElement user) => user.GetProperty("shopId").GetGuid();

    private async Task<JsonElement> ShopWithFake()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        await Ok(HttpMethod.Put, "/api/couriers/Fake", owner, new { credentials = new { apiKey = "fk_test_1234567890" } });
        return owner;
    }

    /// <summary>A confirmed order: one line at <paramref name="price"/> + 60 delivery (inside Dhaka) - <paramref name="advance"/>.</summary>
    private async Task<Guid> ConfirmedOrder(JsonElement owner, decimal price, decimal advance)
    {
        var product = await Ok(HttpMethod.Post, "/api/products", owner, new { name = "Polo shirt", variants = new[] { new { sku = "POLO-" + Guid.NewGuid().ToString("N")[..6], price } } });
        var created = await Ok(HttpMethod.Post, "/api/orders", owner, new
        {
            phone = "01912345678",
            customerName = "Rahima Begum",
            deliveryAddress = "House 4, Road 2, Mirpur",
            isInsideDhaka = true,
            source = "Facebook",
            advancePaid = advance,
            lines = new[] { new { variantId = product.GetProperty("variants")[0].GetProperty("id").GetString(), quantity = 1 } },
        });
        var orderId = created.GetProperty("order").GetProperty("id").GetGuid();
        await Ok(HttpMethod.Post, $"/api/orders/{orderId}/confirm", owner);
        return orderId;
    }

    /// <summary>Booked with the Fake courier, delivered via the status sync job, <paramref name="daysAgo"/> days before now.</summary>
    private async Task<Guid> DeliveredOrder(JsonElement owner, decimal price, decimal advance, int daysAgo)
    {
        var orderId = await ConfirmedOrder(owner, price, advance);
        var booked = await Ok(HttpMethod.Post, $"/api/orders/{orderId}/book", owner, new { courier = "Fake" });

        var consignment = booked.GetProperty("shipment").GetProperty("consignmentId").GetString()!;
        factory.Services.GetRequiredService<FakeCourierControl>().SetStatus(consignment, ParcelStatus.Delivered, factory.Clock.GetUtcNow());
        await factory.Services.GetRequiredService<CourierStatusJobs>().SyncShopAsync(ShopId(owner), CourierProvider.Fake, Ct);

        // The sync stamps "now"; back-date the Delivered step to give the parcel its age.
        var deliveredAt = factory.Clock.GetUtcNow().AddDays(-daysAgo);
        await InShop(owner, db => db.Database.ExecuteSqlAsync(
            $"UPDATE order_status_changes SET changed_at = {deliveredAt} WHERE order_id = {orderId} AND \"to\" = 'Delivered'", Ct));
        return orderId;
    }

    /// <summary>Stands in for payout matching (#28): the order moves Delivered -> CodSettled through the domain.</summary>
    private Task Settle(JsonElement owner, Guid orderId) => InShop(owner, async db =>
    {
        var order = await db.Orders.SingleAsync(o => o.Id == orderId, Ct);
        order.TransitionTo(OrderStatus.CodSettled, by: null, factory.Clock.GetUtcNow());
        return await db.SaveChangesAsync(Ct);
    });

    private async Task InShop(JsonElement owner, Func<HishabDbContext, Task<int>> action)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(ShopId(owner));
        await action(scope.ServiceProvider.GetRequiredService<HishabDbContext>());
    }

    [Fact]
    public async Task Summary_reconciles_delivered_cod_against_settled_orders_with_aged_outstanding()
    {
        var owner = await ShopWithFake();
        await DeliveredOrder(owner, price: 650m, advance: 0m, daysAgo: 1);          // 710.00 due, young
        await DeliveredOrder(owner, price: 1200.50m, advance: 200m, daysAgo: 10);   // 1060.50 due, overdue
        var paid = await DeliveredOrder(owner, price: 400m, advance: 0m, daysAgo: 3); // 460.00 due, settled
        await Settle(owner, paid);
        await ConfirmedOrder(owner, price: 999m, advance: 0m); // not delivered: nothing expected yet

        var summary = await Ok(HttpMethod.Get, "/api/money/cod-summary", owner);

        var fake = summary.GetProperty("couriers").EnumerateArray().ShouldHaveSingleItem();
        fake.GetProperty("courier").GetString().ShouldBe("Fake");
        fake.GetProperty("deliveredParcels").GetInt32().ShouldBe(3);
        fake.GetProperty("expected").GetDecimal().ShouldBe(2230.50m);
        fake.GetProperty("collected").GetDecimal().ShouldBe(460m);
        fake.GetProperty("outstanding").GetDecimal().ShouldBe(1770.50m);
        fake.GetProperty("outstandingParcels").GetInt32().ShouldBe(2);
        fake.GetProperty("oldestOutstandingDays").GetInt32().ShouldBe(10);
        fake.GetProperty("outstandingByAge").EnumerateArray()
            .Select(b => (b.GetProperty("age").GetString(), b.GetProperty("parcels").GetInt32(), b.GetProperty("amount").GetDecimal()))
            .ShouldBe([("Days0To3", 1, 710m), ("Days4To7", 0, 0m), ("Days8To14", 1, 1060.50m), ("Days15Plus", 0, 0m)]);
        summary.GetProperty("expected").GetDecimal().ShouldBe(2230.50m);
        summary.GetProperty("outstanding").GetDecimal().ShouldBe(1770.50m);
    }

    [Fact]
    public async Task Another_shop_sees_none_of_this_shops_cash()
    {
        var shopA = await ShopWithFake();
        await DeliveredOrder(shopA, price: 650m, advance: 0m, daysAgo: 2);
        var shopB = await factory.SignInAsync(_client, Ct);

        var summary = await Ok(HttpMethod.Get, "/api/money/cod-summary", shopB);

        summary.GetProperty("couriers").GetArrayLength().ShouldBe(0);
        summary.GetProperty("expected").GetDecimal().ShouldBe(0m);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Moderator")]
    [InlineData("Packer")]
    public async Task Only_the_owner_sees_the_shops_money(string role)
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var staff = await factory.InviteAndSignInAsync(_client, owner, role, Ct);

        (await Send(HttpMethod.Get, "/api/money/cod-summary", staff)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Get, "/api/money/cod-summary", owner)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
