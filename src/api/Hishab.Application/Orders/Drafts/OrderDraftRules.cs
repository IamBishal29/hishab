using System.ComponentModel;
using Hishab.Domain.Customers;
using Hishab.Domain.Orders;

namespace Hishab.Application.Orders.Drafts;

/// <summary>
/// The shape the model must answer in (sent to it as a JSON schema). Every field is optional: the model
/// is a helper, and an empty answer is a valid one. Nothing here is trusted until <see cref="OrderDraftRules"/>
/// has checked it.
/// </summary>
public sealed class ChatExtraction
{
    [Description("The buyer's name as written in the chat, or null.")]
    public string? CustomerName { get; init; }

    [Description("The token (for example PHONE_1) of the buyer's own number, or null. Never write a number.")]
    public string? PhoneToken { get; init; }

    [Description("The full delivery address from the chat, or null.")]
    public string? DeliveryAddress { get; init; }

    [Description("True if the address is inside Dhaka city, false if outside, null if unclear.")]
    public bool? IsInsideDhaka { get; init; }

    [Description("The items the buyer wants.")]
    public IReadOnlyList<ChatExtractionItem>? Items { get; init; }

    [Description("Other delivery instructions from the buyer (call time, gift wrap), or null.")]
    public string? Note { get; init; }
}

public sealed class ChatExtractionItem
{
    [Description("The item as the buyer described it.")]
    public string? Description { get; init; }

    [Description("The catalogue ref (for example V3) of the matching item, or null if none matches.")]
    public string? CatalogueRef { get; init; }

    [Description("How many the buyer wants; 1 if not said.")]
    public int? Quantity { get; init; }
}

/// <summary>
/// Turns the model's answer into a draft the seller can trust the shape of. The model's output is data,
/// never instructions: a phone can only be one found in the chat, an item can only be one of this shop's
/// variants, text is trimmed to the order's limits, and anything else the model says is ignored. Pure.
/// </summary>
public static class OrderDraftRules
{
    public const int MaxLines = 20;
    public const int DescriptionMaxLength = 200;

    /// <param name="extraction">The model's answer; null when it could not be read.</param>
    /// <param name="catalogue">Catalogue ref (V1, V2, ...) to variant ID, exactly as shown to the model.</param>
    /// <param name="catalogueTruncated">The model was shown only part of the catalogue.</param>
    public static OrderDraft Build(
        ChatExtraction? extraction,
        MaskedChat chat,
        IReadOnlyDictionary<string, Guid> catalogue,
        bool catalogueTruncated)
    {
        var warnings = new List<string>();
        if (extraction is null)
        {
            warnings.Add(DraftWarnings.NotUnderstood);
        }

        var phone = PickPhone(extraction?.PhoneToken, chat, warnings);
        var lines = BuildLines(extraction?.Items, chat, catalogue, warnings);

        if (catalogueTruncated && warnings.Contains(DraftWarnings.UnmatchedItem))
        {
            warnings.Add(DraftWarnings.CatalogueTruncated);
        }

        return new OrderDraft(
            phone,
            CleanText(extraction?.CustomerName, chat, Customer.NameMaxLength),
            CleanText(extraction?.DeliveryAddress, chat, Order.AddressMaxLength),
            extraction?.IsInsideDhaka,
            lines,
            CleanText(extraction?.Note, chat, Order.NoteMaxLength),
            warnings);
    }

    private static string? PickPhone(string? token, MaskedChat chat, List<string> warnings)
    {
        switch (chat.Phones.Count)
        {
            case 0:
                warnings.Add(DraftWarnings.PhoneNotFound);
                return null;
            case 1:
                // Only one number in the chat: it is the buyer's, whatever the model said.
                return chat.Phones.Values.Single().Value;
            default:
                warnings.Add(DraftWarnings.SeveralPhones);
                var key = token?.Trim().Trim('[', ']');
                return key is not null && chat.Phones.TryGetValue(key, out var picked) ? picked.Value : null;
        }
    }

    private static List<OrderDraftLine> BuildLines(
        IReadOnlyList<ChatExtractionItem>? items,
        MaskedChat chat,
        IReadOnlyDictionary<string, Guid> catalogue,
        List<string> warnings)
    {
        var lines = new List<OrderDraftLine>();
        foreach (var item in (items ?? []).Take(MaxLines))
        {
            var description = CleanText(item.Description, chat, DescriptionMaxLength);
            Guid? variantId = item.CatalogueRef?.Trim() is { } reference && catalogue.TryGetValue(reference, out var id) ? id : null;
            if (description is null && variantId is null)
            {
                continue; // nothing to show
            }

            var quantity = item.Quantity ?? 1;
            if (quantity is < 1 or > OrderLine.MaxQuantity)
            {
                quantity = Math.Clamp(quantity, 1, OrderLine.MaxQuantity);
                AddOnce(warnings, DraftWarnings.QuantityAdjusted);
            }

            if (variantId is null)
            {
                AddOnce(warnings, DraftWarnings.UnmatchedItem);
            }

            // The order form allows each variant once: merge repeats into one line.
            var same = variantId is null ? -1 : lines.FindIndex(l => l.VariantId == variantId);
            if (same >= 0)
            {
                var merged = Math.Min(lines[same].Quantity + quantity, OrderLine.MaxQuantity);
                lines[same] = lines[same] with { Quantity = merged };
                continue;
            }

            lines.Add(new OrderDraftLine(variantId, description ?? string.Empty, quantity));
        }

        if (lines.Count == 0)
        {
            warnings.Add(DraftWarnings.NoItems);
        }

        return lines;
    }

    /// <summary>Puts masked numbers back, drops control characters, trims and cuts to the field's limit.</summary>
    private static string? CleanText(string? text, MaskedChat chat, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var unmasked = ChatPhoneMasker.Unmask(text, chat);
        var cleaned = new string([.. unmasked.Select(c => char.IsControl(c) ? ' ' : c)]).Trim();
        if (cleaned.Length > maxLength)
        {
            cleaned = cleaned[..maxLength].TrimEnd();
        }

        return cleaned.Length == 0 ? null : cleaned;
    }

    private static void AddOnce(List<string> warnings, string code)
    {
        if (!warnings.Contains(code))
        {
            warnings.Add(code);
        }
    }
}
