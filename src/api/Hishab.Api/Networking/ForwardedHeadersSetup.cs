using Microsoft.AspNetCore.HttpOverrides;

namespace Hishab.Api.Networking;

/// <summary>Section "ForwardedHeaders": reading the client's address from a hosting platform's proxies.</summary>
public sealed class ProxyOptions
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Off by default: only behind proxies we control. Off, a client could fake X-Forwarded-For; on but
    /// missing behind a proxy, every visitor shares the proxy's address (one per-IP rate limit for all).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How many proxies in front of the app append to X-Forwarded-For. The app trusts exactly that many
    /// entries from the right; anything further left was written by the client and is ignored.
    /// </summary>
    public int ForwardLimit { get; set; } = 1;
}

public static class ForwardedHeadersSetup
{
    public static IServiceCollection AddHishabForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ProxyOptions>()
            .Bind(configuration.GetSection(ProxyOptions.SectionName))
            .Validate(o => o.ForwardLimit is >= 1 and <= 5, "ForwardedHeaders:ForwardLimit must be 1 to 5.")
            .ValidateOnStart();

        var options = configuration.GetSection(ProxyOptions.SectionName).Get<ProxyOptions>() ?? new ProxyOptions();
        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            forwarded.ForwardLimit = options.ForwardLimit;

            // A platform's proxies have no fixed addresses, and the app is reachable only through them
            // (the container port is not public), so the count above is the trust boundary, not an IP list.
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
        });
        return services;
    }

    /// <summary>First in the pipeline when enabled: rate limits and logs must see the client, not the proxy.</summary>
    public static IApplicationBuilder UseHishabForwardedHeaders(this WebApplication app)
    {
        var enabled = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProxyOptions>>().Value.Enabled;
        return enabled ? app.UseForwardedHeaders() : app;
    }
}
