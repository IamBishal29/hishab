using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hangfire;
using Hishab.Api.Controllers;
using Hishab.Domain.Couriers;
using Hishab.Infrastructure.Couriers.Fake;
using Hishab.Infrastructure.Jobs;
using Hishab.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.IntegrationTests.Couriers;

/// <summary>The courier status sync, run directly (the test factory starts no Hangfire worker).</summary>
public class StatusSyncTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset _deliveredAt = new(2026, 11, 21, 9, 30, 0, TimeSpan.Zero);

    private readonly HttpClient _client = factory.CreateClient();

    private CourierStatusJobs Jobs => factory.Services.GetRequiredService<CourierStatusJobs>();

    private FakeCourierControl Fake => factory.Services.GetRequiredService<FakeCourierControl>();

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

        var response = await _client.SendAsync(request, Ct);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    private static Guid ShopId(JsonElement user) => user.GetProperty("shopId").GetGuid();

    private async Task<JsonElement> ShopWithFake()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        await Send(HttpMethod.Put, "/api/couriers/Fake", owner, new { credentials = new { apiKey = "fk_test_1234567890" } });
        return owner;
    }

    /// <summary>A confirmed order booked with the Fake courier: (order ID, consignment ID).</summary>
    private async Task<(Guid OrderId, string Consignment)> BookedOrder(JsonElement owner)
    {
        var product = await Json(await Send(HttpMethod.Post, "/api/products", owner, new { name = "Polo shirt", variants = new[] { new { sku = "POLO-" + Guid.NewGuid().ToString("N")[..6], price = 650m } } }));
        var created = await Json(await Send(HttpMethod.Post, "/api/orders", owner, new
        {
            phone = "01912345678",
            customerName = "Rahima Begum",
            deliveryAddress = "House 4, Road 2, Mirpur",
            isInsideDhaka = true,
            source = "Facebook",
            lines = new[] { new { variantId = product.GetProperty("variants")[0].GetProperty("id").GetString(), quantity = 1 } },
        }));
        var orderId = created.GetProperty("order").GetProperty("id").GetGuid();
        await Send(HttpMethod.Post, $"/api/orders/{orderId}/confirm", owner);
        var booked = await Json(await Send(HttpMethod.Post, $"/api/orders/{orderId}/book", owner, new { courier = "Fake" }));
        return (orderId, booked.GetProperty("shipment").GetProperty("consignmentId").GetString()!);
    }

    private async Task<JsonElement> OrderOf(JsonElement owner, Guid orderId) =>
        (await Json(await Send(HttpMethod.Get, $"/api/orders/{orderId}", owner))).GetProperty("order");

    [Fact]
    public async Task A_delivered_parcel_moves_the_order_through_in_transit_to_delivered_with_history()
    {
        var owner = await ShopWithFake();
        var (orderId, consignment) = await BookedOrder(owner);
        Fake.SetStatus(consignment, ParcelStatus.Delivered, _deliveredAt);

        await Jobs.SyncShopAsync(ShopId(owner), CourierProvider.Fake, Ct);

        var order = await OrderOf(owner, orderId);
        order.GetProperty("status").GetString().ShouldBe("Delivered");
        var history = order.GetProperty("history").EnumerateArray().TakeLast(2).ToList();
        history.Select(h => h.GetProperty("to").GetString()).ShouldBe(["InTransit", "Delivered"]); // no step skipped
        history.ShouldAllBe(h => h.GetProperty("note").GetString() == "Auto-synced from Fake: Delivered");
        history.ShouldAllBe(h => h.GetProperty("changedBy").ValueKind == JsonValueKind.Null); // the system, not a user
    }

    [Fact]
    public async Task Running_the_sync_twice_changes_nothing_the_second_time()
    {
        var owner = await ShopWithFake();
        var (orderId, consignment) = await BookedOrder(owner);
        Fake.SetStatus(consignment, ParcelStatus.InTransit, _deliveredAt);
        await Jobs.SyncShopAsync(ShopId(owner), CourierProvider.Fake, Ct);
        var first = await OrderOf(owner, orderId);

        await Jobs.SyncShopAsync(ShopId(owner), CourierProvider.Fake, Ct);

        var second = await OrderOf(owner, orderId);
        second.GetProperty("status").GetString().ShouldBe("InTransit");
        second.GetProperty("history").GetArrayLength().ShouldBe(first.GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task A_cancelled_parcel_leaves_the_order_for_staff_and_is_not_asked_about_again()
    {
        var owner = await ShopWithFake();
        var (orderId, consignment) = await BookedOrder(owner);
        Fake.SetStatus(consignment, ParcelStatus.Cancelled, _deliveredAt);

        await Jobs.SyncShopAsync(ShopId(owner), CourierProvider.Fake, Ct);

        (await OrderOf(owner, orderId)).GetProperty("status").GetString().ShouldBe("Booked"); // no such move in the lifecycle
        (await Jobs.FindWorkAsync(Ct)).ShouldNotContain((ShopId(owner), CourierProvider.Fake)); // finished at the courier
    }

    [Fact]
    public async Task A_shops_sync_never_touches_another_shops_orders()
    {
        var shopA = await ShopWithFake();
        var shopB = await ShopWithFake();
        var (theirOrder, theirConsignment) = await BookedOrder(shopB);
        Fake.SetStatus(theirConsignment, ParcelStatus.Delivered, _deliveredAt);

        await Jobs.SyncShopAsync(ShopId(shopA), CourierProvider.Fake, Ct);

        (await OrderOf(shopB, theirOrder)).GetProperty("status").GetString().ShouldBe("Booked");
    }

    [Fact]
    public async Task The_dispatcher_enqueues_one_job_per_shop_with_parcels_on_the_road()
    {
        var owner = await ShopWithFake();
        await BookedOrder(owner);
        await BookedOrder(owner); // two parcels, still one job for the shop

        await Jobs.DispatchAsync(Ct);

        var enqueued = factory.Services.GetRequiredService<JobStorage>().GetMonitoringApi().EnqueuedJobs("default", 0, 1_000)
            .Select(j => j.Value.Job)
            .Where(j => j.Method.Name == nameof(CourierStatusJobs.SyncShopAsync) && (Guid)j.Args[0] == ShopId(owner))
            .ToList();
        enqueued.ShouldHaveSingleItem().Args[1].ShouldBe(CourierProvider.Fake);
    }

    [Fact]
    public async Task The_hangfire_dashboard_is_not_served_outside_development()
    {
        var response = await _client.GetAsync(new Uri("/hangfire", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
