using Hishab.Infrastructure.Demo;

namespace Hishab.UnitTests.Demo;

public class DemoSeedSetupTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    [InlineData("Demo ")] // a typo is not an allowed environment
    public void Demo_login_is_refused_outside_development_and_demo(string environment)
    {
        var refused = Should.Throw<InvalidOperationException>(() =>
            DemoSeedSetup.EnsureAllowed(new DemoSeedOptions { Login = true }, environment));

        refused.Message.ShouldContain(environment);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")] // a developer's local database is not disposable either
    public void Reset_on_startup_is_refused_outside_the_demo_environment(string environment) =>
        Should.Throw<InvalidOperationException>(() =>
            DemoSeedSetup.EnsureAllowed(new DemoSeedOptions { Seed = true, ResetOnStartup = true }, environment));

    [Fact]
    public void Reset_on_startup_is_refused_without_seeding() =>
        Should.Throw<InvalidOperationException>(() =>
            DemoSeedSetup.EnsureAllowed(new DemoSeedOptions { ResetOnStartup = true }, "Demo"))
            .Message.ShouldContain("Demo:Seed");

    [Theory]
    [InlineData("Demo", true, true, true)]
    [InlineData("demo", true, true, true)]
    [InlineData("Development", true, true, false)]
    [InlineData("Production", false, false, false)]
    public void Allowed_combinations_start(string environment, bool seed, bool login, bool reset) =>
        Should.NotThrow(() =>
            DemoSeedSetup.EnsureAllowed(new DemoSeedOptions { Seed = seed, Login = login, ResetOnStartup = reset }, environment));
}
