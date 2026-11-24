using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Api.Controllers;
using Hishab.Application.Common.Tenancy;
using Hishab.Domain.Couriers;
using Hishab.Infrastructure.Couriers.Fake;
using Hishab.Infrastructure.Jobs;
using Hishab.Infrastructure.Persistence;
using Hishab.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.IntegrationTests.Finance;

/// <summary>Real orders taken through the API to Delivered or Returned (booked with Fake, synced by the job).</summary>
public sealed class FinanceScenario(HishabApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    public HttpClient Client => _client;

    public async Task<HttpResponseMessage> Send(HttpMethod method, string path, JsonElement user, object? body = null)
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

    public async Task<JsonElement> Ok(HttpMethod method, string path, JsonElement user, object? body = null)
    {
        var response = await Send(method, path, user, body);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }

    public static Guid ShopId(JsonElement user) => user.GetProperty("shopId").GetGuid();

    public async Task<JsonElement> ShopWithFake()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        await Ok(HttpMethod.Put, "/api/couriers/Fake", owner, new { credentials = new { apiKey = "fk_test_1234567890" } });
        return owner;
    }

    /// <summary>A confirmed order: one line at <paramref name="price"/> + 60 delivery (inside Dhaka) - <paramref name="advance"/>.</summary>
    public async Task<Guid> ConfirmedOrder(JsonElement owner, decimal price, decimal advance)
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

    /// <summary>Booked with the Fake courier; returns the order and its consignment ID.</summary>
    public async Task<(Guid OrderId, string Consignment)> BookedOrder(JsonElement owner, decimal price, decimal advance = 0m)
    {
        var orderId = await ConfirmedOrder(owner, price, advance);
        var booked = await Ok(HttpMethod.Post, $"/api/orders/{orderId}/book", owner, new { courier = "Fake" });
        return (orderId, booked.GetProperty("shipment").GetProperty("consignmentId").GetString()!);
    }

    /// <summary>Booked with the Fake courier, returned by the buyer (via the status sync job).</summary>
    public async Task<Guid> ReturnedOrder(JsonElement owner, decimal price)
    {
        var (orderId, consignment) = await BookedOrder(owner, price);
        await CourierReports(owner, consignment, ParcelStatus.Returned);
        return orderId;
    }

    /// <summary>Booked with the Fake courier, delivered via the status sync job, <paramref name="daysAgo"/> days before now.</summary>
    public async Task<(Guid OrderId, string Consignment)> DeliveredOrder(JsonElement owner, decimal price, decimal advance = 0m, int daysAgo = 0)
    {
        var (orderId, consignment) = await BookedOrder(owner, price, advance);
        await CourierReports(owner, consignment, ParcelStatus.Delivered);

        // The sync stamps "now"; back-date the Delivered step to give the parcel its age.
        var deliveredAt = factory.Clock.GetUtcNow().AddDays(-daysAgo);
        await InShop(owner, db => db.Database.ExecuteSqlAsync(
            $"UPDATE order_status_changes SET changed_at = {deliveredAt} WHERE order_id = {orderId} AND \"to\" = 'Delivered'", Ct));
        return (orderId, consignment);
    }

    /// <summary>The courier reports a new status and the sync job picks it up.</summary>
    public async Task CourierReports(JsonElement owner, string consignment, ParcelStatus status)
    {
        factory.Services.GetRequiredService<FakeCourierControl>().SetStatus(consignment, status, factory.Clock.GetUtcNow());
        await factory.Services.GetRequiredService<CourierStatusJobs>().SyncShopAsync(ShopId(owner), CourierProvider.Fake, Ct);
    }

    public async Task InShop(JsonElement owner, Func<HishabDbContext, Task<int>> action)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(ShopId(owner));
        await action(scope.ServiceProvider.GetRequiredService<HishabDbContext>());
    }
}
