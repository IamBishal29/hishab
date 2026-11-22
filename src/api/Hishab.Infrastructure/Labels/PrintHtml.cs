using System.Globalization;
using System.Text.Encodings.Web;

namespace Hishab.Infrastructure.Labels;

/// <summary>
/// What every printed document shares (ADR 0008): the embedded Bangla font as a data: URI (no
/// file or network access while rendering) and one HTML encoder, so values typed by sellers or
/// copied from buyers always stay text, never markup.
/// </summary>
internal static class PrintHtml
{
    private static readonly Lazy<string> _font = new(() =>
    {
        using var stream = typeof(PrintHtml).Assembly.GetManifestResourceStream("Hishab.Labels.NotoSansBengali.ttf")
            ?? throw new InvalidOperationException("The print font is not embedded.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Convert.ToBase64String(copy.ToArray());
    });

    /// <summary>The <c>@font-face</c> rule for Noto Sans Bengali (about 600 kB of base64).</summary>
    public static string FontFace =>
        "@font-face{font-family:'Noto Sans Bengali';src:url(data:font/ttf;base64," + _font.Value + ") format('truetype');}";

    public static int FontLength => _font.Value.Length;

    /// <summary>HTML-encodes a value for element content or a quoted attribute.</summary>
    public static string E(string value) => HtmlEncoder.Default.Encode(value);

    /// <summary>Taka without a currency code: 1,360 or 1,360.5 (the ৳ sign is printed by the template).</summary>
    public static string Taka(decimal amount) => amount.ToString("#,##0.##", CultureInfo.InvariantCulture);
}
