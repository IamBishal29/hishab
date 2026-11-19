using System.Text.RegularExpressions;

namespace Hishab.Application.Common.Privacy;

/// <summary>
/// Masks Bangladeshi mobile numbers for logs and support screens: <c>01712****78</c>.
/// Customer phone numbers are personal data and must never be logged in plain text.
/// </summary>
public static partial class PhoneMasker
{
    private const string Stars = "****";

    /// <summary>Matches BD mobile numbers in free text: 01XXXXXXXXX, 8801XXXXXXXXX, +880 1XXX-XXXXXX.</summary>
    [GeneratedRegex(@"(?<!\d)(?:\+?88[\s-]?)?0[\s-]?1[3-9](?:[\s-]?\d){8}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex BdMobileNumber();

    /// <summary>Masks one phone number. Unrecognised input keeps only its last two digits.</summary>
    public static string Mask(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var digits = new string([.. phone.Where(char.IsAsciiDigit)]);
        if (digits.StartsWith("880", StringComparison.Ordinal) && digits.Length == 13)
        {
            digits = digits[2..]; // 8801712345678 -> 01712345678
        }

        return digits.Length == 11
            ? string.Concat(digits.AsSpan(0, 5), Stars, digits.AsSpan(9, 2))
            : string.Concat(Stars, digits.Length >= 2 ? digits[^2..] : string.Empty);
    }

    /// <summary>Masks every BD mobile number found inside a longer text.</summary>
    public static string MaskAll(string text) =>
        string.IsNullOrEmpty(text) ? text : BdMobileNumber().Replace(text, m => Mask(m.Value));

    /// <summary>True when the text contains something that looks like a BD mobile number.</summary>
    public static bool ContainsPhone(string text) => !string.IsNullOrEmpty(text) && BdMobileNumber().IsMatch(text);
}
