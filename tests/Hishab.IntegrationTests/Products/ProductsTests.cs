using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Application.Common.Tenancy;
using Hishab.Infrastructure.Persistence;
using Hishab.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.IntegrationTests.Products;

public class ProductsTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    private static object Polo(string sku = "POLO-M-RED", decimal? cost = 400m) => new
    {
        name = "Polo shirt",
        variants = new object[]
        {
            new { sku, size = "M", colour = "Red", price = 650m, costPrice = cost },
            new { sku = sku + "-L", size = "L", colour = "Red", price = 650m, costPrice = cost },
        },
    };

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, JsonElement user, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = AuthSession.Bearer(user);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    private async Task<(JsonElement Owner, JsonElement Product)> OwnerWithProduct(string sku = "POLO-M-RED")
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var created = await Send(HttpMethod.Post, "/api/products", owner, Polo(sku));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (owner, await Json(created));
    }

    private static string Id(JsonElement e) => e.GetProperty("id").GetString()!;

    private static JsonElement FirstVariant(JsonElement product) => product.GetProperty("variants")[0];

    [Fact]
    public async Task Owner_creates_a_product_and_sees_cost_prices()
    {
        var (_, product) = await OwnerWithProduct();

        product.GetProperty("variants").GetArrayLength().ShouldBe(2);
        FirstVariant(product).GetProperty("sku").GetString().ShouldBe("POLO-M-RED");
        FirstVariant(product).GetProperty("costPrice").GetDecimal().ShouldBe(400m);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Moderator")]
    [InlineData("Packer")]
    public async Task Staff_read_the_catalogue_without_cost_prices(string role)
    {
        var (owner, product) = await OwnerWithProduct();
        var staff = await factory.InviteAndSignInAsync(_client, owner, role, Ct);

        var seen = await Json(await Send(HttpMethod.Get, $"/api/products/{Id(product)}", staff));

        FirstVariant(seen).GetProperty("price").GetDecimal().ShouldBe(650m);
        FirstVariant(seen).GetProperty("costPrice").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("Moderator")]
    [InlineData("Packer")]
    public async Task Only_owners_and_managers_edit_the_catalogue(string role)
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var staff = await factory.InviteAndSignInAsync(_client, owner, role, Ct);

        (await Send(HttpMethod.Post, "/api/products", staff, Polo(cost: null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Manager_cannot_set_a_cost_price()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var manager = await factory.InviteAndSignInAsync(_client, owner, "Manager", Ct);

        var response = await Send(HttpMethod.Post, "/api/products", manager, Polo());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Json(response)).GetProperty("detail").GetString().ShouldBe("Only the owner can set cost prices.");
        (await Send(HttpMethod.Post, "/api/products", manager, Polo(cost: null))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Manager_price_change_keeps_the_owners_cost()
    {
        var (owner, product) = await OwnerWithProduct();
        var manager = await factory.InviteAndSignInAsync(_client, owner, "Manager", Ct);
        var variant = FirstVariant(product);

        var update = await Send(HttpMethod.Put, $"/api/products/{Id(product)}/variants/{Id(variant)}", manager,
            new { sku = "POLO-M-RED", size = "M", colour = "Red", price = 700m });

        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ownerView = await Json(await Send(HttpMethod.Get, $"/api/products/{Id(product)}", owner));
        FirstVariant(ownerView).GetProperty("price").GetDecimal().ShouldBe(700m);
        FirstVariant(ownerView).GetProperty("costPrice").GetDecimal().ShouldBe(400m);
    }

    [Fact]
    public async Task Search_matches_name_or_sku_case_insensitively_with_paging()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        await Send(HttpMethod.Post, "/api/products", owner, Polo("POLO-1"));
        await Send(HttpMethod.Post, "/api/products", owner, new { name = "Jamdani saree", variants = new[] { new { sku = "SAREE-9", price = 4500m } } });
        await Send(HttpMethod.Post, "/api/products", owner, new { name = "Panjabi 50% off", variants = new[] { new { sku = "PNJ-1", price = 900m } } });

        var byName = await Json(await Send(HttpMethod.Get, "/api/products?search=JAMDANI", owner));
        var bySku = await Json(await Send(HttpMethod.Get, "/api/products?search=saree-9", owner));
        var literalPercent = await Json(await Send(HttpMethod.Get, "/api/products?search=50%25", owner));
        var firstPage = await Json(await Send(HttpMethod.Get, "/api/products?pageSize=2", owner));

        byName.GetProperty("items").EnumerateArray().ShouldHaveSingleItem().GetProperty("name").GetString().ShouldBe("Jamdani saree");
        bySku.GetProperty("totalCount").GetInt32().ShouldBe(1);
        literalPercent.GetProperty("items").EnumerateArray().ShouldHaveSingleItem().GetProperty("name").GetString().ShouldBe("Panjabi 50% off");
        firstPage.GetProperty("totalCount").GetInt32().ShouldBe(3);
        firstPage.GetProperty("items").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Sku_is_unique_in_the_shop_until_its_product_is_deleted()
    {
        var (owner, product) = await OwnerWithProduct("DUP-1");

        (await Send(HttpMethod.Post, "/api/products", owner, Polo("dup-1"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await Send(HttpMethod.Delete, $"/api/products/{Id(product)}", owner)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Send(HttpMethod.Post, "/api/products", owner, Polo("DUP-1"))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Deleted_product_is_hidden_but_kept_for_history()
    {
        var (owner, product) = await OwnerWithProduct();

        await Send(HttpMethod.Delete, $"/api/products/{Id(product)}", owner);

        (await Send(HttpMethod.Get, $"/api/products/{Id(product)}", owner)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Json(await Send(HttpMethod.Get, "/api/products", owner))).GetProperty("totalCount").GetInt32().ShouldBe(0);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(owner.GetProperty("shopId").GetGuid());
        var db = scope.ServiceProvider.GetRequiredService<HishabDbContext>();
        var row = await db.Products.IgnoreQueryFilters([HishabDbContext.SoftDeleteFilter]).Include(p => p.Variants)
            .SingleAsync(p => p.Id == Guid.Parse(Id(product)), Ct);
        row.DeletedAt.ShouldNotBeNull();
        row.Variants.ShouldAllBe(v => v.DeletedAt != null);
    }

    [Fact]
    public async Task Last_variant_cannot_be_removed()
    {
        var (owner, product) = await OwnerWithProduct();
        var variants = product.GetProperty("variants");

        (await Send(HttpMethod.Delete, $"/api/products/{Id(product)}/variants/{Id(variants[0])}", owner)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Send(HttpMethod.Delete, $"/api/products/{Id(product)}/variants/{Id(variants[1])}", owner)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Nested_validation_errors_are_keyed_like_the_json()
    {
        var owner = await factory.SignInAsync(_client, Ct);

        var response = await Send(HttpMethod.Post, "/api/products", owner,
            new { name = "Polo", variants = new[] { new { sku = "OK-1", price = 1m }, new { sku = "BAD", price = -5m } } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("errors").EnumerateObject().Select(e => e.Name).ShouldBe(["variants[1].price"]);
    }

    [Fact]
    public async Task Another_shops_products_are_not_found()
    {
        var (_, product) = await OwnerWithProduct();
        var otherOwner = await factory.SignInAsync(_client, Ct);
        var variant = FirstVariant(product);

        (await Send(HttpMethod.Get, $"/api/products/{Id(product)}", otherOwner)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Put, $"/api/products/{Id(product)}/variants/{Id(variant)}", otherOwner,
            new { sku = "HIJACK", price = 1m })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Delete, $"/api/products/{Id(product)}", otherOwner)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
