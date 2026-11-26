namespace Hishab.Infrastructure.Orders.Drafts;

public enum ChatAssistantProvider
{
    /// <summary>Not set up: drafting from a chat answers 503 and the rest of the app works as usual.</summary>
    None,
    Gemini,
}

/// <summary>Section <c>Ai</c>. The API key comes from user-secrets or an environment variable only.</summary>
public sealed class ChatAssistantOptions
{
    public const string SectionName = "Ai";

    public ChatAssistantProvider Provider { get; set; } = ChatAssistantProvider.None;

    /// <summary>Gemini model ID; a fast, cheap model is enough for extraction.</summary>
    public string Model { get; set; } = "gemini-2.5-flash";

    public string? ApiKey { get; set; }

    /// <summary>How long one draft may take before the seller is told to enter the order by hand.</summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Catalogue variants shown to the model (the rest cannot be matched; the draft says so).</summary>
    public int MaxCatalogueItems { get; set; } = 300;
}
