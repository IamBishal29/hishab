using System.Threading.RateLimiting;
using Hishab.Api.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hishab.Api.RateLimiting;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>OTP requests (send + verify) allowed per client IP per minute.</summary>
    public int OtpPermitsPerMinute { get; set; } = 10;

    /// <summary>Chat-to-order drafts allowed per shop per minute (shares one AI key across all shops).</summary>
    public int ChatDraftPermitsPerMinute { get; set; } = 6;
}

public static class RateLimitingSetup
{
    public const string OtpPolicy = "otp";
    public const string ChatDraftPolicy = "chat-draft";

    public static IServiceCollection AddHishabRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .Validate(o => o.OtpPermitsPerMinute > 0, "RateLimiting:OtpPermitsPerMinute must be positive.")
            .Validate(o => o.ChatDraftPermitsPerMinute > 0, "RateLimiting:ChatDraftPermitsPerMinute must be positive.")
            .ValidateOnStart();

        var options = configuration.GetSection(RateLimitingOptions.SectionName).Get<RateLimitingOptions>()
            ?? new RateLimitingOptions();

        services.AddRateLimiter(limiter =>
        {
            // Per client IP. Behind a reverse proxy this needs ForwardedHeaders first, or
            // every request shares the proxy's address (deployment issue, not MVP).
            limiter.AddPolicy(OtpPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.OtpPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            // Per shop (the signed-in user's tenant claim): one busy shop cannot use up the AI quota of all others.
            limiter.AddPolicy(ChatDraftPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                http.User.FindFirst(Hishab.Application.Common.Security.HishabClaimTypes.TenantId)?.Value ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.ChatDraftPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = DomainExceptionHandler.RetryAfterSeconds(retryAfter);
                }

                await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == ChatDraftPolicy
                            ? "Your shop has drafted many orders this minute. Please wait a minute and try again."
                            : "Too many attempts from this network. Please wait a minute and try again.",
                    },
                });
            };
        });

        return services;
    }
}
