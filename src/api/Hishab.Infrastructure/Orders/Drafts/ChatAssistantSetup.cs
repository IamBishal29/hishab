using Google.GenAI;
using Hishab.Application.Orders.Drafts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hishab.Infrastructure.Orders.Drafts;

internal static class ChatAssistantSetup
{
    /// <summary>
    /// Registers the order drafting service and one <see cref="IChatClient"/> picked by <c>Ai:Provider</c>.
    /// The provider sits behind the Microsoft.Extensions.AI abstraction, so swapping Gemini for another model
    /// is a new case here, not a change to the service. No logging middleware is added on purpose: it would
    /// write the chat (names, addresses) to the logs.
    /// </summary>
    public static IServiceCollection AddChatAssistant(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.AddOptions<ChatAssistantOptions>()
            .Bind(configuration.GetSection(ChatAssistantOptions.SectionName))
            .Validate(o => o.Provider is ChatAssistantProvider.None || !string.IsNullOrWhiteSpace(o.ApiKey),
                "Ai:ApiKey is required when Ai:Provider is set (user-secrets or environment variable).")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Model), "Ai:Model is required.")
            .Validate(o => o.TimeoutSeconds is > 0 and <= 120, "Ai:TimeoutSeconds must be between 1 and 120.")
            .Validate(o => o.MaxCatalogueItems is > 0 and <= 2_000, "Ai:MaxCatalogueItems must be between 1 and 2000.")
            .ValidateOnStart();

        services.AddSingleton<IChatClient>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<ChatAssistantOptions>>().Value;
            return o.Provider switch
            {
                ChatAssistantProvider.Gemini => new Client(apiKey: o.ApiKey).AsIChatClient(o.Model),
                _ => new NotConfiguredChatClient(),
            };
        });
        services.AddScoped<IOrderDraftService, OrderDraftService>();
        return services;
    }
}

/// <summary>Stands in when no model is set up, so the app starts without an AI key.</summary>
internal sealed class NotConfiguredChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw ChatAssistantUnavailableException.NotConfigured();

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw ChatAssistantUnavailableException.NotConfigured();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
