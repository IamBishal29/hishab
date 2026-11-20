using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Hishab.IntegrationTests;

/// <summary>
/// The web client is generated from <c>src/web/openapi.json</c>. This test fails when the API
/// contract changes and the snapshot was not updated, so frontend and backend cannot drift.
/// Update it with <c>HISHAB_UPDATE_OPENAPI=1 dotnet test</c>, then <c>npm run api:generate</c>.
/// </summary>
public class OpenApiSnapshotTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private const string UpdateVariable = "HISHAB_UPDATE_OPENAPI";

    [Fact]
    public async Task Committed_openapi_snapshot_matches_the_api()
    {
        var current = await CurrentDocumentAsync();
        var path = SnapshotPath();

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            await File.WriteAllTextAsync(path, current, TestContext.Current.CancellationToken);
            return;
        }

        var committed = File.Exists(path) ? Normalize(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)) : "";
        committed.ShouldBe(current, $"The API contract changed. Run `{UpdateVariable}=1 dotnet test` in src/api, then `npm run api:generate` in src/web.");
    }

    private async Task<string> CurrentDocumentAsync()
    {
        var document = await factory.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);

        foreach (var testPath in document.Paths.Keys.Where(p => p.StartsWith("/api/_test", StringComparison.Ordinal)).ToList())
        {
            document.Paths.Remove(testPath); // test-only endpoints are not part of the contract
        }

        document.Servers?.Clear(); // the test host's address is not part of the contract either
        return Normalize(await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1, cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>XML-doc text carries the checkout's line endings (CRLF on Windows, LF in CI).</summary>
    private static string Normalize(string json) => json.Replace("\\r\\n", "\\n", StringComparison.Ordinal).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";

    private static string SnapshotPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root (global.json) not found."), "src", "web", "openapi.json");
    }
}
