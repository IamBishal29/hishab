using System.Buffers;
using System.Globalization;
using System.Text;

namespace Hishab.Infrastructure.Export;

/// <summary>
/// Writes RFC 4180 CSV that Excel and Google Sheets open correctly: UTF-8 with a BOM (so Bangla is
/// not garbled), CRLF line ends, and fields quoted when they hold a comma, quote or line break.
/// Text typed by people (names, addresses, notes) is neutralised against CSV injection: a cell
/// starting with = + - @ tab or CR is a formula to a spreadsheet, so it gets a leading apostrophe.
/// Values we format ourselves (numbers, dates) are written as they are, so they stay numbers.
/// </summary>
internal sealed class CsvWriter(TextWriter writer)
{
    private static readonly SearchValues<char> _formulaStarts = SearchValues.Create("=+-@\t\r");
    private static readonly SearchValues<char> _needsQuotes = SearchValues.Create(",\"\r\n");

    private bool _rowStarted;

    /// <summary>UTF-8 with a byte order mark: Excel needs it to read Bangla text as UTF-8.</summary>
    public static Encoding Encoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>A cell someone typed: neutralised against formulas, then quoted if needed.</summary>
    public CsvWriter Text(string? value) => Raw(Neutralise(value));

    /// <summary>A value we produced (an enum, an ID): quoted if needed, never altered.</summary>
    public CsvWriter Raw(string? value)
    {
        if (_rowStarted)
        {
            writer.Write(',');
        }

        _rowStarted = true;
        if (string.IsNullOrEmpty(value))
        {
            return this;
        }

        if (value.AsSpan().ContainsAny(_needsQuotes))
        {
            writer.Write('"');
            writer.Write(value.Replace("\"", "\"\"", StringComparison.Ordinal));
            writer.Write('"');
        }
        else
        {
            writer.Write(value);
        }

        return this;
    }

    public CsvWriter Number(decimal? value) => Raw(value?.ToString("0.##", CultureInfo.InvariantCulture));

    public CsvWriter Number(int value) => Raw(value.ToString(CultureInfo.InvariantCulture));

    public async Task EndRowAsync()
    {
        await writer.WriteAsync("\r\n");
        _rowStarted = false;
    }

    public async Task HeaderAsync(params string[] names)
    {
        foreach (var name in names)
        {
            Raw(name);
        }

        await EndRowAsync();
    }

    internal static string? Neutralise(string? value) =>
        string.IsNullOrEmpty(value) || !_formulaStarts.Contains(value[0]) ? value : "'" + value;
}
