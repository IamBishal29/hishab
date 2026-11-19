using System.Net;

namespace Hishab.IntegrationTests;

public class HealthTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    [Fact]
    public async Task Health_returns_healthy_when_database_is_reachable()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
    }
}
