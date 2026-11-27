using Hishab.Application.Shops.Auth;
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

    /// <summary>
    /// One-click sign-in as the demo shop's people (<c>/api/auth/demo</c>), for a hosted demo with no SMS
    /// gateway. Allowed only in <see cref="DemoSeedSetup.AllowedEnvironments"/>.
    /// </summary>
    public bool Login { get; set; }

    /// <summary>
    /// Empties every app table and seeds again at startup. Demo environment only, and the database must
    /// hold nothing but the demo: this is how a public demo gets back to clean data.
    /// </summary>
    public bool ResetOnStartup { get; set; }
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
        services.AddSingleton<DemoReset>();
        services.AddScoped<IDemoLoginService, DemoLoginService>();

        if (configuration.GetSection(DemoSeedOptions.SectionName).Get<DemoSeedOptions>()?.Seed == true)
        {
            // Only when seeding: the seeder moves this clock through the demo timeline (see SeedClock).
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new SeedClock(TimeProvider.System)));
        }
    }

    /// <summary>
    /// Fails closed: refuses to start when a demo switch is on where it must not be, so a copied setting
    /// can never put fake orders into a real database, open a password-less login, or wipe real data.
    /// </summary>
    public static void EnsureAllowed(DemoSeedOptions options, string environmentName)
    {
        if ((options.Seed || options.Login) && !IsAllowedIn(environmentName))
        {
            throw new InvalidOperationException(
                $"Demo:Seed or Demo:Login is on in the '{environmentName}' environment. Demo features are only allowed in: {string.Join(", ", AllowedEnvironments)}.");
        }

        if (options.ResetOnStartup && !string.Equals(environmentName, DemoEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            // Not even Development: a developer's local database is not disposable.
            throw new InvalidOperationException(
                $"Demo:ResetOnStartup empties the database and is only allowed in the '{DemoEnvironment}' environment, not '{environmentName}'.");
        }

        if (options.ResetOnStartup && !options.Seed)
        {
            throw new InvalidOperationException("Demo:ResetOnStartup needs Demo:Seed, or the demo would start empty.");
        }
    }

    /// <summary>
    /// Checks the demo switches, then (when on) empties the database and seeds the demo shop. Runs before
    /// the host serves requests or starts jobs, which is what makes moving the seed clock safe.
    /// </summary>
    public static async Task SeedDemoDataIfEnabledAsync(this IServiceProvider services, string environmentName, CancellationToken cancellationToken = default)
    {
        var options = services.GetRequiredService<IOptions<DemoSeedOptions>>().Value;
        EnsureAllowed(options, environmentName);

        if (options.ResetOnStartup)
        {
            await services.GetRequiredService<DemoReset>().ResetAsync(cancellationToken);
        }

        if (options.Seed)
        {
            await services.GetRequiredService<DemoSeeder>().SeedAsync(cancellationToken);
        }
    }
}
