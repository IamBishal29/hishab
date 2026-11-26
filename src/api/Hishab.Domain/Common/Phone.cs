using System.Diagnostics.CodeAnalysis;

namespace Hishab.Domain.Common;

/// <summary>
/// A Bangladeshi mobile number, normalised to E.164 (<c>+8801XXXXXXXXX</c>). Two spellings
/// of the same number ("01712-345678", "+880 1712 345678") are equal, so one seller can
/// never end up with two accounts. <see cref="ToString"/> is masked: a phone number is
/// personal data and must not reach logs in clear text by accident.
/// </summary>
public sealed record Phone
{
    public const string InvalidMessage = "Enter a Bangladeshi mobile number, for example 01712345678.";

    /// <summary>
    /// Fictional numbers for demo data: 010 is not issued to any operator, so +880 10 0000 0xxx can never
    /// reach a real person (like the US 555-01xx range). Only these 1,000 numbers of 010 are accepted.
    /// </summary>
    public const string DemoBlock = "+8801000000";

    private const string DemoLocalPrefix = "01000000";

    private Phone(string value) => Value = value;

    /// <summary>E.164 form, e.g. +8801712345678. Use for storage and SMS only, never for logs.</summary>
    public string Value { get; }

    /// <summary>True for a fictional demo number (<see cref="DemoBlock"/>).</summary>
    public bool IsDemo => Value.StartsWith(DemoBlock, StringComparison.Ordinal);

    /// <summary>Masked local form for logs and support screens, e.g. 01712****78.</summary>
    public string Masked => string.Concat(Value.AsSpan(3, 5), "****", Value.AsSpan(Value.Length - 2));

    public static bool TryParse(string? input, [NotNullWhen(true)] out Phone? phone)
    {
        phone = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var digits = new List<char>(input.Length);
        foreach (var c in input.Trim())
        {
            if (char.IsAsciiDigit(c))
            {
                digits.Add(c);
            }
            else if (c == '+' && digits.Count > 0)
            {
                return false; // '+' only as the international prefix
            }
            else if (c is not (' ' or '-' or '(' or ')' or '+'))
            {
                return false; // letters or other symbols: not a phone number
            }
        }

        var number = new string([.. digits]);
        var local = number switch
        {
            { Length: 13 } when number.StartsWith("880", StringComparison.Ordinal) => number[2..], // 8801712345678
            { Length: 11 } => number,
            _ => null,
        };

        // Mobile numbers: 01 followed by an operator digit 3-9 and 8 more digits, or the demo block.
        var isOperator = local is not null && local.StartsWith("01", StringComparison.Ordinal) && local[2] is >= '3' and <= '9';
        if (local is null || !(isOperator || local.StartsWith(DemoLocalPrefix, StringComparison.Ordinal)))
        {
            return false;
        }

        phone = new Phone("+88" + local);
        return true;
    }

    /// <exception cref="DomainValidationException">The input is not a Bangladeshi mobile number.</exception>
    public static Phone Parse(string? input, string field = "phone") =>
        TryParse(input, out var phone) ? phone : throw new DomainValidationException(field, InvalidMessage);

    /// <summary>The <paramref name="index"/>-th demo number (0-999), e.g. 1 -> +8801000000001.</summary>
    public static Phone Demo(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, 999);
        return Parse(DemoLocalPrefix + index.ToString("000", System.Globalization.CultureInfo.InvariantCulture));
    }

    public override string ToString() => Masked;
}
