using System.Text.RegularExpressions;
using Hishab.Application.Orders;
using Hishab.Infrastructure.Labels;

namespace Hishab.UnitTests.Orders;

public partial class LabelHtmlTests
{
    private static ShippingLabel Label(int number = 1043, string name = "রহিমা বেগম", string address = "বাড়ি ৪, রোড ২, মিরপুর ১০, ঢাকা", string? note = null) =>
        new("Rina's Boutique", "Shop 12, Dhanmondi", number, name, "01712345678", address, true, 1360m, 2, "Pathao", "DL121224VS8TTJ", null, note);

    [GeneratedRegex("<section class=\"page\">")]
    private static partial Regex PageSection();

    [GeneratedRegex("<article class=\"label\">")]
    private static partial Regex LabelArticle();

    [Fact]
    public void Text_typed_by_sellers_stays_text_and_never_becomes_markup()
    {
        var html = LabelHtml.Build([Label(name: "<script>alert(1)</script>", address: "<img src=http://169.254.169.254/latest>", note: "\"><b>x</b>")], LabelFormat.Thermal);

        html.ShouldNotContain("<script>");
        html.ShouldNotContain("<img");
        html.ShouldNotContain("<b>x</b>");
        html.ShouldContain("&lt;script&gt;");
    }

    [Fact]
    public void Bangla_text_and_the_embedded_font_are_in_the_document()
    {
        var html = LabelHtml.Build([Label()], LabelFormat.A4);

        html.ShouldContain("lang=\"bn\"");
        html.ShouldContain("@font-face{font-family:'Noto Sans Bengali';src:url(data:font/ttf;base64,AAEAAA"); // TrueType magic, inlined
        html.ShouldContain("৳ 1,360");
    }

    [Theory]
    [InlineData(LabelFormat.Thermal, 9, 9)]
    [InlineData(LabelFormat.A4, 8, 1)]
    [InlineData(LabelFormat.A4, 9, 2)]
    [InlineData(LabelFormat.A4, 17, 3)]
    public void Labels_are_grouped_into_pages_for_the_paper(LabelFormat format, int labels, int pages)
    {
        var html = LabelHtml.Build([.. Enumerable.Range(1, labels).Select(n => Label(number: n))], format);

        PageSection().Count(html).ShouldBe(pages);
        LabelArticle().Count(html).ShouldBe(labels);
        LabelHtml.PagesFor(labels, format).ShouldBe(pages);
    }

    [Fact]
    public void The_page_size_matches_the_paper()
    {
        LabelHtml.Build([Label()], LabelFormat.Thermal).ShouldContain("@page{size:100mm 150mm;margin:0}");
        LabelHtml.Build([Label()], LabelFormat.A4).ShouldContain("@page{size:A4;");
    }
}
