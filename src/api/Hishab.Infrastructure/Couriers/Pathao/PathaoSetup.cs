using Hishab.Application.Couriers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Hishab.Infrastructure.Couriers.Pathao;

internal static class PathaoSetup
{
    public static void AddPathao(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PathaoOptions>()
            .Bind(configuration.GetSection(PathaoOptions.SectionName))
            .Validate(o => o.IsValid(), "Couriers:Pathao needs an https BaseUrl, DeliveryType 12 or 48 and a weight of 0-10 kg.")
            .ValidateOnStart();

        services.AddMemoryCache();
        services.AddSingleton<PathaoTokens>();
        services.AddSingleton<ICourierProvider, PathaoCourierProvider>();

        services.AddHttpClient(PathaoCourierProvider.HttpClientName, (sp, http) =>
                http.BaseAddress = sp.GetRequiredService<IOptions<PathaoOptions>>().Value.BaseUrl)
            .AddStandardResilienceHandler(pipeline =>
            {
                // Reads and token calls are retried with backoff; a booking (POST) never is:
                // Pathao may have created the parcel even when we saw a timeout. Our own retry
                // (same reference, intent record) decides that, not the HTTP layer.
                pipeline.Retry.DisableForUnsafeHttpMethods();
                pipeline.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                pipeline.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);

                // Open after half the calls in 30 s fail (at least 10 calls), stay open 30 s: a
                // courier that is down gets a rest, and our users get "try again" at once.
                pipeline.CircuitBreaker.FailureRatio = 0.5;
                pipeline.CircuitBreaker.MinimumThroughput = 10;
                pipeline.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                pipeline.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
            });
    }
}
