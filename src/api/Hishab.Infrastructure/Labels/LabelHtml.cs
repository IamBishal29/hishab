using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Hishab.Application.Orders;

namespace Hishab.Infrastructure.Labels;

/// <summary>What one label shows. Recipient details are personal data: they go on paper, never into logs.</summary>
internal sealed record ShippingLabel(
    string ShopName,
    string? PickupAddress,
    int OrderNumber,
    string RecipientName,
    string RecipientPhone,
    string Address,
    bool IsInsideDhaka,
    decimal CodAmount,
    int ItemCount,
    string Courier,
    string ConsignmentId,
    string? TrackingCode,
    string? Note);

/// <summary>
/// The label sheet as HTML for Chromium to print. Every value is HTML-encoded: names, addresses
/// and notes are typed by sellers (or copied from buyers) and must stay text, never markup.
/// The font is inlined as a data: URI, so rendering needs no file or network access.
/// </summary>
internal static class LabelHtml
{
    public const int LabelsPerA4Page = 8;

    private static readonly Lazy<string> _font = new(() =>
    {
        using var stream = typeof(LabelHtml).Assembly.GetManifestResourceStream("Hishab.Labels.NotoSansBengali.ttf")
            ?? throw new InvalidOperationException("The label font is not embedded.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Convert.ToBase64String(copy.ToArray());
    });

    public static int PagesFor(int labels, LabelFormat format) =>
        format == LabelFormat.Thermal ? labels : (labels + LabelsPerA4Page - 1) / LabelsPerA4Page;

    public static string Build(IReadOnlyList<ShippingLabel> labels, LabelFormat format)
    {
        var html = new StringBuilder(_font.Value.Length + (labels.Count * 1_200));
        html.Append("<!DOCTYPE html><html lang=\"bn\"><head><meta charset=\"utf-8\"><style>")
            .Append("@font-face{font-family:'Noto Sans Bengali';src:url(data:font/ttf;base64,").Append(_font.Value).Append(") format('truetype');}")
            .Append(format == LabelFormat.Thermal ? ThermalCss : A4Css)
            .Append(SharedCss)
            .Append("</style></head><body>");

        var perPage = format == LabelFormat.Thermal ? 1 : LabelsPerA4Page;
        foreach (var page in labels.Chunk(perPage))
        {
            html.Append("<section class=\"page\">");
            foreach (var label in page)
            {
                AppendLabel(html, label);
            }

            html.Append("</section>");
        }

        return html.Append("</body></html>").ToString();
    }

    private static void AppendLabel(StringBuilder html, ShippingLabel label)
    {
        html.Append("<article class=\"label\">")
            .Append("<header><strong>").Append(E(label.ShopName)).Append("</strong>")
            .Append("<span>#").Append(label.OrderNumber.ToString(CultureInfo.InvariantCulture)).Append("</span></header>");
        if (label.PickupAddress is { Length: > 0 } pickup)
        {
            html.Append("<p class=\"from\">").Append(E(pickup)).Append("</p>");
        }

        html.Append("<div class=\"to\"><p class=\"name\">").Append(E(label.RecipientName)).Append("</p>")
            .Append("<p class=\"phone\">").Append(E(label.RecipientPhone)).Append("</p>")
            .Append("<p class=\"address\">").Append(E(label.Address)).Append("</p>")
            .Append("<p class=\"zone\">").Append(label.IsInsideDhaka ? "Inside Dhaka / ঢাকার ভিতরে" : "Outside Dhaka / ঢাকার বাইরে").Append("</p></div>")
            .Append("<div class=\"cod\"><span>COD / ক্যাশ অন ডেলিভারি</span><strong>৳ ")
            .Append(label.CodAmount.ToString("#,##0.##", CultureInfo.InvariantCulture)).Append("</strong>")
            .Append("<span>Items: ").Append(label.ItemCount.ToString(CultureInfo.InvariantCulture)).Append("</span></div>")
            .Append("<footer><span>").Append(E(label.Courier)).Append("</span>")
            .Append("<strong class=\"consignment\">").Append(E(label.ConsignmentId)).Append("</strong>");
        if (label.TrackingCode is { Length: > 0 } tracking && tracking != label.ConsignmentId)
        {
            html.Append("<span>Tracking: ").Append(E(tracking)).Append("</span>");
        }

        html.Append("</footer>");
        if (label.Note is { Length: > 0 } note)
        {
            html.Append("<p class=\"note\">").Append(E(note)).Append("</p>");
        }

        html.Append("</article>");
    }

    private static string E(string value) => HtmlEncoder.Default.Encode(value);

    private const string SharedCss =
        "*{box-sizing:border-box;margin:0;padding:0}"
        + "body{font-family:'Noto Sans Bengali',sans-serif;color:#000;font-size:10pt;line-height:1.3}"
        + ".label{border:1px solid #000;padding:3mm;display:flex;flex-direction:column;gap:1.5mm;overflow:hidden}"
        + ".label header{display:flex;justify-content:space-between;font-size:11pt}"
        + ".from,.note,.zone{font-size:8pt}"
        + ".to .name{font-weight:700;font-size:12pt}.to .phone{font-size:12pt;letter-spacing:.5px}"
        + ".address{overflow-wrap:anywhere}"
        + ".cod{display:flex;justify-content:space-between;align-items:baseline;border:1px solid #000;padding:1mm 2mm}"
        + ".cod strong{font-size:14pt}"
        + ".label footer{display:flex;justify-content:space-between;align-items:baseline;gap:2mm;margin-top:auto}"
        + ".consignment{font-size:12pt;letter-spacing:.5px}";

    // One label per 100 × 150 mm page.
    private const string ThermalCss =
        "@page{size:100mm 150mm;margin:0}"
        + ".page{width:100mm;height:150mm;padding:4mm;page-break-after:always;display:flex}"
        + ".page:last-child{page-break-after:auto}.label{width:100%;height:100%}";

    // Eight labels per A4 page: 2 columns × 4 rows of 95 × 68 mm.
    private const string A4Css =
        "@page{size:A4;margin:8mm 10mm}"
        + ".page{display:grid;grid-template-columns:repeat(2,95mm);grid-auto-rows:68mm;gap:3mm;page-break-after:always}"
        + ".page:last-child{page-break-after:auto}";
}
