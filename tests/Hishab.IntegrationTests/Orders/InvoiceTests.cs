using System.Net;
using System.Text;
using Hishab.IntegrationTests.Support;

namespace Hishab.IntegrationTests.Orders;

/// <summary>
/// GET /api/orders/invoices (#68). Shares the label tests' helpers; rendering needs Playwright's
/// Chromium like labels. Validation, permission and isolation tests fail before rendering.
/// </summary>
public partial class LabelTests
{
    private Task<HttpResponseMessage> Invoices(System.Text.Json.JsonElement user, IEnumerable<Guid> ids, string format = "A5") =>
        Send(HttpMethod.Get, $"/api/orders/invoices?format={format}&" + string.Join("&", ids.Select(id => $"ids={id}")), user);

    [Fact]
    public async Task Invoices_print_one_page_per_order_booked_or_not_with_the_bangla_font()
    {
        var owner = await ShopWithFake();
        var ids = new[] { await Order(owner), await Order(owner, book: false), await Order(owner) };

        var response = await Invoices(owner, ids);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        response.Content.Headers.ContentDisposition!.FileName.ShouldBe("invoices-a5.pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        if (Environment.GetEnvironmentVariable("HISHAB_INVOICE_SAMPLE") is { Length: > 0 } samplePath)
        {
            await File.WriteAllBytesAsync(samplePath, bytes, Ct); // opt-in: look at a real invoice
        }

        var pdf = Encoding.Latin1.GetString(bytes);
        pdf.ShouldStartWith("%PDF-");
        PdfPage().Count(pdf).ShouldBe(3);
        pdf.ShouldContain("NotoSansBengali");
    }

    [Fact]
    public async Task A4_invoices_are_still_one_per_page()
    {
        var owner = await ShopWithFake();
        var ids = new[] { await Order(owner, book: false), await Order(owner, book: false) };

        var response = await Invoices(owner, ids, "A4");

        response.Content.Headers.ContentDisposition!.FileName.ShouldBe("invoices-a4.pdf");
        PdfPage().Count(Encoding.Latin1.GetString(await response.Content.ReadAsByteArrayAsync(Ct))).ShouldBe(2);
    }

    [Fact]
    public async Task Cancelled_orders_are_named_and_nothing_is_printed()
    {
        var owner = await ShopWithFake();
        var cancelled = await Order(owner, book: false);
        (await Send(HttpMethod.Post, $"/api/orders/{cancelled}/cancel", owner)).EnsureSuccessStatusCode();

        var response = await Invoices(owner, [cancelled]);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("errors").GetProperty("orderIds")[0].GetString()!
            .ShouldStartWith("Cancelled orders have no invoice: #");
    }

    [Fact]
    public async Task Another_shops_invoice_is_not_found_and_moderators_cannot_print()
    {
        var owner = await ShopWithFake();
        var moderator = await factory.InviteAndSignInAsync(_client, owner, "Moderator", Ct);
        var otherShop = await ShopWithFake();
        var theirs = await Order(otherShop, book: false);

        (await Invoices(owner, [theirs])).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Invoices(moderator, [Guid.NewGuid()])).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Invoices(owner, Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
