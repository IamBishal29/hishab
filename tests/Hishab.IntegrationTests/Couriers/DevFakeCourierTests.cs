using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Api.Controllers;
using Hishab.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;

namespace Hishab.IntegrationTests.Couriers;

/// <summary>The API as compose runs it for demos and the e2e test.</summary>
public sealed class DemoEnvironmentApiFactory : HishabApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Demo");
    }
}

public class DevFakeCourierTests(DemoEnvironmentApiFactory factory) : IClassFixture<DemoEnvironmentApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task The_fake_courier_moves_a_booked_parcel_and_the_order_follows_through_the_status_sync()
    {
        var owner = await ShopWithFakeCourier();
        var orderId = await BookedOrder(owner);

        var inTransit = await Move(owner, orderId, "InTransit");
        var delivered = await Move(owner, orderId, "Delivered");

        inTransit.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Status(inTransit)).ShouldBe("InTransit");
        (await Status(delivered)).ShouldBe("Delivered");
    }

    [Fact]
    public async Task Another_shops_order_is_not_found()
    {
        var owner = await ShopWithFakeCourier();
        var orderId = await BookedOrder(owner);
        var stranger = await ShopWithFakeCourier();

        (await Move(stranger, orderId, "Delivered")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_order_with_no_parcel_yet_is_not_found()
    {
        var owner = await ShopWithFakeCourier();
        var orderId = await NewOrder(owner);

        (await Move(owner, orderId, "Delivered")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<JsonElement> ShopWithFakeCourier()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        (await Send(HttpMethod.Put, "/api/couriers/Fake", owner, new { credentials = new { apiKey = "fk_test_1234567890" } })).EnsureSuccessStatusCode();
        return owner;
    }

    private async Task<Guid> NewOrder(JsonElement owner)
    {
        var product = await Json(await Send(HttpMethod.Post, "/api/products", owner, new { name = "Polo shirt", variants = new[] { new { sku = "POLO-" + Guid.NewGuid().ToString("N")[..6], price = 650m } } }));
        var created = await Send(HttpMethod.Post, "/api/orders", owner, new
        {
            phone = AuthSession.NewPhone(),
            customerName = "Rahima Begum",
            deliveryAddress = "House 4, Road 2, Mirpur",
            isInsideDhaka = true,
            source = "Facebook",
            lines = new[] { new { variantId = product.GetProperty("variants")[0].GetProperty("id").GetString(), quantity = 1 } },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await Json(created)).GetProperty("order").GetProperty("id").GetGuid();
    }

    private async Task<Guid> BookedOrder(JsonElement owner)
    {
        var id = await NewOrder(owner);
        (await Send(HttpMethod.Post, $"/api/orders/{id}/confirm", owner)).EnsureSuccessStatusCode();
        (await Send(HttpMethod.Post, $"/api/orders/{id}/book", owner, new { courier = "Fake" })).EnsureSuccessStatusCode();
        return id;
    }

    private Task<HttpResponseMessage> Move(JsonElement user, Guid orderId, string status) =>
        Send(HttpMethod.Post, $"/api/dev/fake-courier/orders/{orderId}/status", user, new { status });

    private static async Task<string?> Status(HttpResponseMessage response) =>
        (await Json(response)).GetProperty("order").GetProperty("status").GetString();

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
}

/// <summary>Outside Development and Demo the route does not exist at all.</summary>
public class DevFakeCourierAbsentTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    [Fact]
    public async Task The_dev_route_does_not_exist_in_other_environments()
    {
        var client = factory.CreateClient();
        var owner = await factory.SignInAsync(client, TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/dev/fake-courier/orders/{Guid.NewGuid()}/status")
        {
            Content = JsonContent.Create(new { status = "Delivered" }),
        };
        request.Headers.Authorization = AuthSession.Bearer(owner);

        (await client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
