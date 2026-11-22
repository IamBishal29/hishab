using System.Globalization;
using System.Text;
using Hishab.Application.Orders;

namespace Hishab.Infrastructure.Labels;

/// <summary>
/// What one invoice shows: an allow-list for the buyer. There is deliberately no cost, SKU or
/// margin here, so the template cannot print what it never receives (a guard test checks it).
/// Buyer details are personal data: they go on paper, never into logs.
/// </summary>
internal sealed record Invoice(
    string ShopName,
    string? PickupAddress,
    int OrderNumber,
    DateOnly Date,
    string CustomerName,
    string CustomerPhone,
    string? Address,
    IReadOnlyList<InvoiceLine> Lines,
    decimal Subtotal,
    decimal DeliveryCharge,
    decimal Total,
    decimal AdvancePaid,
    decimal CodAmount,
    string? Note);

/// <summary>One line at the selling price snapshot taken when the order was made.</summary>
internal sealed record InvoiceLine(string Name, string? Variant, int Quantity, decimal UnitPrice, decimal LineTotal);

/// <summary>Invoices as HTML for Chromium to print, one per page; every value HTML-encoded.</summary>
internal static class InvoiceHtml
{
    public static string Build(IReadOnlyList<Invoice> invoices, InvoiceFormat format)
    {
        var html = new StringBuilder(PrintHtml.FontLength + (invoices.Count * 2_500));
        html.Append("<!DOCTYPE html><html lang=\"bn\"><head><meta charset=\"utf-8\"><style>")
            .Append(PrintHtml.FontFace)
            .Append(format == InvoiceFormat.A4 ? "@page{size:A4;margin:14mm}" : "@page{size:A5;margin:10mm}")
            .Append(Css)
            .Append("</style></head><body>");

        foreach (var invoice in invoices)
        {
            AppendInvoice(html, invoice);
        }

        return html.Append("</body></html>").ToString();
    }

    private static void AppendInvoice(StringBuilder html, Invoice invoice)
    {
        html.Append("<section class=\"page\"><header><div><h1>").Append(E(invoice.ShopName)).Append("</h1>");
        if (invoice.PickupAddress is { Length: > 0 } pickup)
        {
            html.Append("<p class=\"muted\">").Append(E(pickup)).Append("</p>");
        }

        html.Append("</div><div class=\"meta\"><h2>Invoice / চালান</h2><p>#")
            .Append(invoice.OrderNumber.ToString(CultureInfo.InvariantCulture)).Append("</p><p>")
            .Append(invoice.Date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)).Append("</p></div></header>")
            .Append("<div class=\"to\"><p class=\"muted\">Bill to / ক্রেতা</p><p class=\"name\">").Append(E(invoice.CustomerName)).Append("</p>")
            .Append("<p>").Append(E(invoice.CustomerPhone)).Append("</p>");
        if (invoice.Address is { Length: > 0 } address)
        {
            html.Append("<p>").Append(E(address)).Append("</p>");
        }

        html.Append("</div><table><thead><tr><th>Item / পণ্য</th><th class=\"n\">Qty</th><th class=\"n\">Price</th><th class=\"n\">Total</th></tr></thead><tbody>");
        foreach (var line in invoice.Lines)
        {
            html.Append("<tr><td>").Append(E(line.Name));
            if (line.Variant is { Length: > 0 } variant)
            {
                html.Append(" <span class=\"muted\">(").Append(E(variant)).Append(")</span>");
            }

            html.Append("</td><td class=\"n\">").Append(line.Quantity.ToString(CultureInfo.InvariantCulture))
                .Append("</td><td class=\"n\">").Append(Taka(line.UnitPrice))
                .Append("</td><td class=\"n\">").Append(Taka(line.LineTotal)).Append("</td></tr>");
        }

        html.Append("</tbody></table><dl class=\"totals\">");
        Row(html, "Subtotal / মোট পণ্য", invoice.Subtotal);
        Row(html, "Delivery / ডেলিভারি চার্জ", invoice.DeliveryCharge);
        Row(html, "Total / সর্বমোট", invoice.Total);
        if (invoice.AdvancePaid > 0)
        {
            Row(html, "Advance paid / অগ্রিম পরিশোধ", -invoice.AdvancePaid);
        }

        html.Append("</dl><p class=\"cod\"><span>To pay on delivery / ডেলিভারিতে পরিশোধ</span><strong>")
            .Append(Taka(invoice.CodAmount)).Append("</strong></p>");
        if (invoice.Note is { Length: > 0 } note)
        {
            html.Append("<p class=\"note\">").Append(E(note)).Append("</p>");
        }

        html.Append("<footer>Thank you for your order / আপনার অর্ডারের জন্য ধন্যবাদ</footer></section>");
    }

    private static void Row(StringBuilder html, string label, decimal amount) =>
        html.Append("<dt>").Append(label).Append("</dt><dd>").Append(Taka(amount)).Append("</dd>");

    private static string Taka(decimal amount) => (amount < 0 ? "− ৳ " : "৳ ") + PrintHtml.Taka(Math.Abs(amount));

    private static string E(string value) => PrintHtml.E(value);

    private const string Css =
        "*{box-sizing:border-box;margin:0;padding:0}"
        + "body{font-family:'Noto Sans Bengali',sans-serif;color:#000;font-size:10pt;line-height:1.35}"
        + ".page{display:flex;flex-direction:column;gap:5mm;page-break-after:always}.page:last-child{page-break-after:auto}"
        + "header{display:flex;justify-content:space-between;gap:6mm;border-bottom:1px solid #000;padding-bottom:3mm}"
        + "h1{font-size:15pt}h2{font-size:12pt}.meta{text-align:right}.muted{color:#444;font-size:8.5pt}"
        + ".to .name{font-weight:700;font-size:11pt}.to p,.note,td{overflow-wrap:anywhere}"
        + "table{width:100%;border-collapse:collapse}th,td{padding:1.5mm 1mm;border-bottom:1px solid #bbb;text-align:left;vertical-align:top}"
        + "th{font-size:8.5pt;border-bottom:1px solid #000}.n{text-align:right;white-space:nowrap}"
        + ".totals{display:grid;grid-template-columns:1fr auto;gap:1mm 6mm;margin-left:auto;min-width:60%}.totals dd{text-align:right}"
        + ".cod{display:flex;justify-content:space-between;align-items:baseline;border:1.5px solid #000;padding:2mm 3mm}"
        + ".cod strong{font-size:14pt}.note{font-size:9pt}footer{margin-top:auto;text-align:center;font-size:8.5pt;color:#444}";
}
