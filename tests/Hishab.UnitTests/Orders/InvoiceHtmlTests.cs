using System.Text.RegularExpressions;
using Hishab.Application.Orders;
using Hishab.Infrastructure.Labels;

namespace Hishab.UnitTests.Orders;

public partial class InvoiceHtmlTests
{
    private static Invoice Invoice(
        int number = 1043,
        string name = "রহিমা বেগম",
        string? address = "বাড়ি ৪, রোড ২, মিরপুর ১০, ঢাকা",
        decimal advance = 0m,
        string product = "নকশি কাঁথা",
        string? variant = "Single",
        string? note = null) =>
        new(
            "Rina's Boutique",
            "Shop 12, Dhanmondi",
            number,
            new DateOnly(2026, 11, 22),
            name,
            "01712345678",
            address,
            [new InvoiceLine(product, variant, 2, 650m, 1300m), new InvoiceLine("Gift wrap", null, 1, 50m, 50m)],
            1350m,
            60m,
            1410m,
            advance,
            1410m - advance,
            note);

    [GeneratedRegex("<section class=\"page\">")]
    private static partial Regex Page();

    [Fact]
    public void Text_typed_by_sellers_or_buyers_stays_text_and_never_becomes_markup()
    {
        var html = InvoiceHtml.Build(
            [Invoice(name: "<script>alert(1)</script>", address: "<img src=x>", product: "<b>Polo</b>", variant: "\"><i>", note: "<iframe>")],
            InvoiceFormat.A5);

        html.ShouldNotContain("<script>");
        html.ShouldNotContain("<img");
        html.ShouldNotContain("<b>Polo</b>");
        html.ShouldNotContain("<i>");
        html.ShouldNotContain("<iframe>");
        html.ShouldContain("&lt;script&gt;");
    }

    [Fact]
    public void Totals_show_delivery_and_what_the_buyer_pays_at_the_door()
    {
        var html = InvoiceHtml.Build([Invoice()], InvoiceFormat.A5);

        html.ShouldContain("<dt>Subtotal / মোট পণ্য</dt><dd>৳ 1,350</dd>");
        html.ShouldContain("<dt>Delivery / ডেলিভারি চার্জ</dt><dd>৳ 60</dd>");
        html.ShouldContain("<dt>Total / সর্বমোট</dt><dd>৳ 1,410</dd>");
        html.ShouldContain("<strong>৳ 1,410</strong>");
        html.ShouldNotContain("Advance paid"); // nothing paid ahead: no row
    }

    [Fact]
    public void An_advance_is_shown_as_paid_and_taken_off_the_amount_to_collect()
    {
        var html = InvoiceHtml.Build([Invoice(advance: 200m)], InvoiceFormat.A5);

        html.ShouldContain("<dt>Advance paid / অগ্রিম পরিশোধ</dt><dd>− ৳ 200</dd>");
        html.ShouldContain("<strong>৳ 1,210</strong>");
    }

    [Theory]
    [InlineData(InvoiceFormat.A5, "@page{size:A5")]
    [InlineData(InvoiceFormat.A4, "@page{size:A4")]
    public void Each_invoice_is_its_own_page_in_the_chosen_size(InvoiceFormat format, string pageRule)
    {
        var html = InvoiceHtml.Build([Invoice(1), Invoice(2), Invoice(3)], format);

        html.ShouldContain(pageRule);
        Page().Count(html).ShouldBe(3);
        html.IndexOf("#1<", StringComparison.Ordinal).ShouldBeLessThan(html.IndexOf("#3<", StringComparison.Ordinal));
    }

    [Fact]
    public void The_embedded_font_date_and_lines_are_in_the_document()
    {
        var html = InvoiceHtml.Build([Invoice(address: null)], InvoiceFormat.A5);

        html.ShouldContain("@font-face{font-family:'Noto Sans Bengali';src:url(data:font/ttf;base64,AAEAAA");
        html.ShouldContain("22 Nov 2026");
        html.ShouldContain("<td class=\"n\">2</td><td class=\"n\">৳ 650</td><td class=\"n\">৳ 1,300</td>");
        html.ShouldNotContain("<p></p>"); // no address: no empty line
    }

    [Theory]
    [InlineData(typeof(Invoice))]
    [InlineData(typeof(InvoiceLine))]
    public void Invoices_cannot_carry_cost_or_margin(Type record)
    {
        // Invoices go to buyers. If a cost-like field is ever added here, a template could print it.
        record.GetProperties().Select(p => p.Name)
            .Where(n => Regex.IsMatch(n, "Cost|Margin|Profit|Sku", RegexOptions.IgnoreCase))
            .ShouldBeEmpty();
    }
}
