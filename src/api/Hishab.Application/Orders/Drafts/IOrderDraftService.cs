namespace Hishab.Application.Orders.Drafts;

/// <summary>
/// Turns a pasted buyer conversation (Messenger, WhatsApp) into a draft order with the help of a
/// language model. A draft is only a suggestion for the quick order form: nothing is saved, and the
/// person taking the order confirms every field before it becomes an order.
/// </summary>
public interface IOrderDraftService
{
    /// <exception cref="ChatAssistantUnavailableException">No model is configured, or it did not answer.</exception>
    Task<OrderDraft> DraftFromChatAsync(string chat, CancellationToken cancellationToken);
}

/// <summary>
/// What the assistant read from the chat, already checked by the server. <see cref="Phone"/> is always a
/// number that appears in the chat; <see cref="OrderDraftLine.VariantId"/> is always one of this shop's
/// active variants. Everything else is free text to review.
/// </summary>
/// <param name="Phone">The buyer's number in E.164 form, or null when the chat has none (or several and none was picked).</param>
/// <param name="IsInsideDhaka">The assistant's guess from the address; null when it could not tell.</param>
/// <param name="Warnings">Stable codes from <see cref="DraftWarnings"/> for the form to show.</param>
public sealed record OrderDraft(
    string? Phone,
    string? CustomerName,
    string? DeliveryAddress,
    bool? IsInsideDhaka,
    IReadOnlyList<OrderDraftLine> Lines,
    string? Note,
    IReadOnlyList<string> Warnings);

/// <param name="VariantId">The matched catalogue variant; null when the item is not in the catalogue (pick it by hand).</param>
/// <param name="Description">The item as the buyer described it.</param>
public sealed record OrderDraftLine(Guid? VariantId, string Description, int Quantity);

/// <summary>Stable warning codes; the web app translates them.</summary>
public static class DraftWarnings
{
    /// <summary>The chat has no Bangladeshi mobile number.</summary>
    public const string PhoneNotFound = "phoneNotFound";

    /// <summary>The chat has more than one number; check that the right one was picked.</summary>
    public const string SeveralPhones = "severalPhones";

    /// <summary>At least one item could not be matched to the catalogue.</summary>
    public const string UnmatchedItem = "unmatchedItem";

    /// <summary>No items were found in the chat.</summary>
    public const string NoItems = "noItems";

    /// <summary>A quantity was missing or out of range and was set to a safe value.</summary>
    public const string QuantityAdjusted = "quantityAdjusted";

    /// <summary>The assistant's answer could not be read; only the phone number was taken from the chat.</summary>
    public const string NotUnderstood = "notUnderstood";

    /// <summary>The catalogue is larger than the assistant is shown; some items may not match.</summary>
    public const string CatalogueTruncated = "catalogueTruncated";
}

/// <summary>The assistant is not set up on this server, or it did not answer in time (HTTP 503).</summary>
public sealed class ChatAssistantUnavailableException(string message) : Domain.Common.DomainException(message)
{
    public static ChatAssistantUnavailableException NotConfigured() =>
        new("Drafting orders from a chat is not set up on this server. Enter the order by hand.");

    public static ChatAssistantUnavailableException NoAnswer() =>
        new("The assistant did not answer. Try again in a moment, or enter the order by hand.");
}
