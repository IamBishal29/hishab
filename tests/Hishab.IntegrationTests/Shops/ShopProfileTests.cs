using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Domain.Shops;
using Hishab.IntegrationTests.Support;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Hishab.IntegrationTests.Shops;

public class ShopProfileTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    private static readonly object _validUpdate = new
    {
        name = "Rahim Fashion",
        logoUrl = "https://example.com/logo.png",
        pickupAddress = "House 12, Road 5, Mirpur 10, Dhaka",
        defaultCourier = "Steadfast",
        deliveryChargeInsideDhaka = 70.50m,
        deliveryChargeOutsideDhaka = 130m,
    };

    private async Task<string> OwnerToken() =>
        (await factory.SignInAsync(_client, Ct)).GetProperty("accessToken").GetString()!;

    private async Task<HttpResponseMessage> Send(HttpMethod method, string? token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, "/shop");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    [Fact]
    public async Task New_shop_has_default_profile_and_delivery_charges()
    {
        var response = await Send(HttpMethod.Get, await OwnerToken());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var shop = await Json(response);
        shop.GetProperty("name").GetString().ShouldBe("My shop");
        shop.GetProperty("deliveryChargeInsideDhaka").GetDecimal().ShouldBe(60m);
        shop.GetProperty("deliveryChargeOutsideDhaka").GetDecimal().ShouldBe(120m);
        shop.GetProperty("currency").GetString().ShouldBe("BDT");
    }

    [Fact]
    public async Task Owner_updates_the_profile_and_reads_it_back()
    {
        var token = await OwnerToken();

        (await Send(HttpMethod.Put, token, _validUpdate)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var shop = await Json(await Send(HttpMethod.Get, token));
        shop.GetProperty("name").GetString().ShouldBe("Rahim Fashion");
        shop.GetProperty("defaultCourier").GetString().ShouldBe("Steadfast");
        shop.GetProperty("deliveryChargeInsideDhaka").GetDecimal().ShouldBe(70.50m);
        shop.GetProperty("pickupAddress").GetString().ShouldBe("House 12, Road 5, Mirpur 10, Dhaka");
    }

    [Fact]
    public async Task Invalid_update_returns_errors_per_field()
    {
        var response = await Send(HttpMethod.Put, await OwnerToken(), new
        {
            name = "",
            logoUrl = "javascript:alert(1)",
            deliveryChargeInsideDhaka = -5m,
            deliveryChargeOutsideDhaka = 12.345m,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await Json(response)).GetProperty("errors");
        errors.EnumerateObject().Select(e => e.Name).ShouldBe(
            ["name", "logoUrl", "deliveryChargeInsideDhaka", "deliveryChargeOutsideDhaka"], ignoreOrder: true);
    }

    [Fact]
    public async Task Posted_id_and_extra_fields_are_ignored()
    {
        var token = await OwnerToken();
        var before = await Json(await Send(HttpMethod.Get, token));

        var response = await Send(HttpMethod.Put, token, new
        {
            id = Guid.NewGuid(),
            tenantId = Guid.NewGuid(),
            createdAt = "2000-01-01T00:00:00Z",
            name = "Over-posted",
            deliveryChargeInsideDhaka = 60m,
            deliveryChargeOutsideDhaka = 120m,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(response)).GetProperty("id").GetGuid().ShouldBe(before.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Anonymous_request_is_unauthorized()
    {
        (await Send(HttpMethod.Get, token: null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(ShopRole.Moderator, HttpStatusCode.Forbidden)]
    [InlineData(ShopRole.Packer, HttpStatusCode.Forbidden)]
    [InlineData(ShopRole.Manager, HttpStatusCode.OK)]
    public async Task Only_owners_and_managers_manage_the_shop(ShopRole role, HttpStatusCode expected)
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var staff = (await factory.InviteAndSignInAsync(_client, owner, role.ToString(), Ct)).GetProperty("accessToken").GetString();

        (await Send(HttpMethod.Put, staff, _validUpdate)).StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task Shop_A_cannot_read_or_change_shop_B()
    {
        var shopA = await factory.SignInAsync(_client, Ct);
        var tokenA = shopA.GetProperty("accessToken").GetString();
        var shopB = await factory.SignInAsync(_client, Ct);
        var tokenB = shopB.GetProperty("accessToken").GetString();

        await Send(HttpMethod.Put, tokenA, _validUpdate);
        var seenByB = await Json(await Send(HttpMethod.Get, tokenB));

        seenByB.GetProperty("id").GetGuid().ShouldBe(shopB.GetProperty("shopId").GetGuid());
        seenByB.GetProperty("name").GetString().ShouldBe("My shop"); // A's update did not touch B

        await Send(HttpMethod.Put, tokenB, new { name = "B renamed", deliveryChargeInsideDhaka = 1m, deliveryChargeOutsideDhaka = 2m });
        (await Json(await Send(HttpMethod.Get, tokenA))).GetProperty("name").GetString().ShouldBe("Rahim Fashion");
    }

    [Fact]
    public async Task OpenApi_documents_the_shop_endpoints_with_examples()
    {
        var provider = factory.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");

        var document = await provider.GetOpenApiDocumentAsync(Ct);

        var json = JsonDocument.Parse(await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, cancellationToken: Ct)).RootElement;
        json.GetProperty("paths").GetProperty("/shop").GetProperty("put").GetProperty("summary").GetString().ShouldNotBeNullOrEmpty();
        var properties = json.GetProperty("components").GetProperty("schemas").GetProperty("UpdateShopRequest").GetProperty("properties");
        properties.GetProperty("name").GetProperty("examples")[0].GetString().ShouldBe("Rahim Fashion"); // OpenAPI 3.1: "examples" array
        properties.GetProperty("deliveryChargeInsideDhaka").GetProperty("examples")[0].GetDecimal().ShouldBe(60m);
    }
}
