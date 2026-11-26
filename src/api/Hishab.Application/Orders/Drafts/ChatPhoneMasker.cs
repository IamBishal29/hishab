using System.Text;
using System.Text.RegularExpressions;
using Hishab.Domain.Common;

namespace Hishab.Application.Orders.Drafts;

/// <summary>
/// A chat with every Bangladeshi mobile number replaced by a token (<c>[PHONE_1]</c>, ...). Phone
/// numbers are found here, on our server, so they never reach the language model; the model only
/// points at a token, and a token can only stand for a number that really is in the chat.
/// </summary>
/// <param name="Text">The chat with tokens in place of numbers; safe to send to the model.</param>
/// <param name="Phones">Token (without brackets) to number, in order of first appearance.</param>
public sealed record MaskedChat(string Text, IReadOnlyDictionary<string, Phone> Phones);

public static partial class ChatPhoneMasker
{
    public const string TokenPrefix = "PHONE_";

    /// <summary>
    /// A candidate number: optional 88 / +88, then 0 and 1 (a space may follow the 0), an operator digit and 8 more digits, with single
    /// spaces or dashes allowed between digits (01712-345678, +880 1712 345678). Not inside a longer
    /// run of digits (an order ID, a bKash transaction number).
    /// </summary>
    [GeneratedRegex(@"(?<![\d+])(?:\+?88[ -]?)?0[ -]?1(?:[3-9](?:[ -]?\d){8}|0(?:[ -]?0){5}(?:[ -]?\d){3})(?!\d)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Candidate();

    public static MaskedChat Mask(string chat)
    {
        // Bangla digits (০-৯) become ASCII digits one for one, so match positions stay valid in the original.
        var ascii = ToAsciiDigits(chat);
        var phones = new Dictionary<string, Phone>(StringComparer.Ordinal);
        var tokenByNumber = new Dictionary<string, string>(StringComparer.Ordinal);
        var text = new StringBuilder(chat.Length);
        var last = 0;

        foreach (Match match in Candidate().Matches(ascii))
        {
            if (!Phone.TryParse(match.Value, out var phone))
            {
                continue;
            }

            if (!tokenByNumber.TryGetValue(phone.Value, out var token))
            {
                token = TokenPrefix + (phones.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                tokenByNumber[phone.Value] = token;
                phones[token] = phone;
            }

            text.Append(chat, last, match.Index - last).Append('[').Append(token).Append(']');
            last = match.Index + match.Length;
        }

        text.Append(chat, last, chat.Length - last);
        return new MaskedChat(text.ToString(), phones);
    }

    /// <summary>Puts the numbers back into model-written text (e.g. "alternate [PHONE_2]") in local form.</summary>
    public static string Unmask(string text, MaskedChat chat)
    {
        // Longest token first, so PHONE_1 never eats the start of PHONE_12.
        foreach (var (token, phone) in chat.Phones.OrderByDescending(p => p.Key.Length).ThenByDescending(p => p.Key, StringComparer.Ordinal))
        {
            text = text.Replace($"[{token}]", phone.Value[3..], StringComparison.Ordinal)
                       .Replace(token, phone.Value[3..], StringComparison.Ordinal);
        }

        return text;
    }

    private static string ToAsciiDigits(string text) => string.Create(text.Length, text, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            span[i] = c is >= '০' and <= '৯' ? (char)('0' + (c - '০')) : c;
        }
    });
}
