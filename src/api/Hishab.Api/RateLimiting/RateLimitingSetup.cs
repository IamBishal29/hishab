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
}

public static class RateLimitingSetup
{
    public const string OtpPolicy = "otp";

    public static IServiceCollection AddHishabRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .Validate(o => o.OtpPermitsPerMinute > 0, "RateLimiting:OtpPermitsPerMinute must be positive.")
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
                        Detail = "Too many attempts from this network. Please wait a minute and try again.",
                    },
                });
            };
        });

        return services;
    }
}
