using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hishab.Api.Controllers;
using Hishab.IntegrationTests.Support;

namespace Hishab.IntegrationTests.Orders;

/// <summary>
/// GET /api/orders/labels. Rendering tests need Playwright's Chromium (CI installs it; locally see
/// README "PDF labels"). Validation, permission and isolation tests fail before rendering.
/// </summary>
public partial class LabelTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    [GeneratedRegex(@"/Type\s*/Page[^s]")]
    private static partial Regex PdfPage();

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

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    private async Task<JsonElement> ShopWithFake()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        (await Send(HttpMethod.Put, "/api/couriers/Fake", owner, new { credentials = new { apiKey = "fk_test_1234567890" } })).EnsureSuccessStatusCode();
        return owner;
    }

    private async Task<Guid> Order(JsonElement owner, bool book = true)
    {
        var product = await Json(await Send(HttpMethod.Post, "/api/products", owner, new { name = "পোলো শার্ট", variants = new[] { new { sku = "POLO-" + Guid.NewGuid().ToString("N")[..6], price = 650m } } }));
        var created = await Json(await Send(HttpMethod.Post, "/api/orders", owner, new
        {
            phone = "01912345678",
            customerName = "রহিমা বেগম",
            deliveryAddress = "বাড়ি ৪, রোড ২, মিরপুর ১০, ঢাকা",
            isInsideDhaka = true,
            source = "Facebook",
            lines = new[] { new { variantId = product.GetProperty("variants")[0].GetProperty("id").GetString(), quantity = 1 } },
        }));
        var id = created.GetProperty("order").GetProperty("id").GetGuid();
        if (book)
        {
            (await Send(HttpMethod.Post, $"/api/orders/{id}/confirm", owner)).EnsureSuccessStatusCode();
            (await Send(HttpMethod.Post, $"/api/orders/{id}/book", owner, new { courier = "Fake" })).EnsureSuccessStatusCode();
        }

        return id;
    }

    private Task<HttpResponseMessage> Labels(JsonElement user, IEnumerable<Guid> ids, string format = "A4") =>
        Send(HttpMethod.Get, $"/api/orders/labels?format={format}&" + string.Join("&", ids.Select(id => $"ids={id}")), user);

    [Fact]
    public async Task Thermal_labels_print_one_page_per_order_with_the_bangla_font_embedded()
    {
        var owner = await ShopWithFake();
        var ids = new[] { await Order(owner), await Order(owner), await Order(owner) };

        var response = await Labels(owner, ids, "Thermal");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        response.Content.Headers.ContentDisposition!.FileName.ShouldBe("labels-thermal.pdf");
        var pdf = Encoding.Latin1.GetString(await response.Content.ReadAsByteArrayAsync(Ct));
        pdf.ShouldStartWith("%PDF-");
        PdfPage().Count(pdf).ShouldBe(3);
        pdf.ShouldContain("NotoSansBengali"); // the embedded font, not a fallback that would break conjuncts
    }

    [Fact]
    public async Task A4_labels_fit_eight_to_a_page()
    {
        var owner = await ShopWithFake();
        var ids = new List<Guid>();
        for (var i = 0; i < 9; i++)
        {
            ids.Add(await Order(owner));
        }

        var pdf = Encoding.Latin1.GetString(await (await Labels(owner, ids)).Content.ReadAsByteArrayAsync(Ct));

        PdfPage().Count(pdf).ShouldBe(2);
    }

    [Fact]
    public async Task Orders_not_booked_yet_are_named_and_nothing_is_printed()
    {
        var owner = await ShopWithFake();
        var notBooked = await Order(owner, book: false);

        var response = await Labels(owner, [notBooked]);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("errors").GetProperty("orderIds")[0].GetString()!.ShouldStartWith("Book these orders with a courier first: #");
    }

    [Fact]
    public async Task Another_shops_order_is_not_found_and_moderators_cannot_print()
    {
        var owner = await ShopWithFake();
        var moderator = await factory.InviteAndSignInAsync(_client, owner, "Moderator", Ct);
        var otherShop = await ShopWithFake();
        var theirs = await Order(otherShop, book: false);

        (await Labels(owner, [theirs])).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Labels(moderator, [Guid.NewGuid()])).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Labels(owner, Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
