using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Hishab.Infrastructure.Demo;

public sealed class DemoSeedOptions
{
    public const string SectionName = "Demo";

    /// <summary>Seed the demo shop at startup. Allowed only in <see cref="DemoSeedSetup.AllowedEnvironments"/>.</summary>
    public bool Seed { get; set; }
}

public static class DemoSeedSetup
{
    /// <summary>The environment for a hosted demo (screenshots, e2e): like Production, but with demo data.</summary>
    public const string DemoEnvironment = "Demo";

    /// <summary>An allow-list, not a deny-list: a new environment ("Staging", a typo) never seeds by accident.</summary>
    public static IReadOnlyList<string> AllowedEnvironments { get; } = ["Development", DemoEnvironment];

    public static bool IsAllowedIn(string environmentName) =>
        AllowedEnvironments.Contains(environmentName, StringComparer.OrdinalIgnoreCase);

    internal static void AddDemoSeed(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DemoSeedOptions>().Bind(configuration.GetSection(DemoSeedOptions.SectionName));
        services.AddSingleton<DemoSeeder>();

        if (configuration.GetSection(DemoSeedOptions.SectionName).Get<DemoSeedOptions>()?.Seed == true)
        {
            // Only when seeding: the seeder moves this clock through the demo timeline (see SeedClock).
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new SeedClock(TimeProvider.System)));
        }
    }

    /// <summary>
    /// Seeds the demo shop when <c>Demo:Seed</c> is on. Refuses to start in any other environment, so a
    /// copied setting can never put fake orders into a real database.
    /// </summary>
    public static async Task SeedDemoDataIfEnabledAsync(this IServiceProvider services, string environmentName, CancellationToken cancellationToken = default)
    {
        if (!services.GetRequiredService<IOptions<DemoSeedOptions>>().Value.Seed)
        {
            return;
        }

        if (!IsAllowedIn(environmentName))
        {
            throw new InvalidOperationException(
                $"Demo:Seed is on in the '{environmentName}' environment. Demo data is only allowed in: {string.Join(", ", AllowedEnvironments)}.");
        }

        await services.GetRequiredService<DemoSeeder>().SeedAsync(cancellationToken);
    }
}
