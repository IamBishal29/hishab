using Hishab.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hishab.Infrastructure.Demo;

/// <summary>
/// Empties every table of the app's EF model in one <c>TRUNCATE ... CASCADE</c>, so the seeder can build the
/// demo shop again from nothing. The table list comes from the model, so a new entity is covered without
/// anyone remembering to add it, and nothing outside the model (Hangfire's schema) is touched. Kept: the
/// migrations history (the schema version) and the Data Protection key ring (keys in use by this process).
/// Only <see cref="DemoSeedSetup.SeedDemoDataIfEnabledAsync"/> calls it, after <see cref="DemoSeedSetup.EnsureAllowed"/>.
/// </summary>
public sealed partial class DemoReset(IServiceScopeFactory scopes, ILogger<DemoReset> logger)
{
    public async Task<int> ResetAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HishabDbContext>();
        var tables = AppTables(db.Model);

        // Identifiers from our own model, quoted; no user input reaches this statement.
        var sql = "TRUNCATE TABLE " + string.Join(", ", tables) + " RESTART IDENTITY CASCADE";
        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);

        LogReset(logger, tables.Count);
        return tables.Count;
    }

    internal static IReadOnlyList<string> AppTables(IModel model) =>
        [.. model.GetEntityTypes()
            .Where(t => t.ClrType != typeof(DataProtectionKey) && t.GetTableName() is not null)
            .Select(t => t.GetSchema() is { } schema
                ? $"{Quote(schema)}.{Quote(t.GetTableName()!)}"
                : Quote(t.GetTableName()!))
            .Distinct()
            .Order(StringComparer.Ordinal)];

    private static string Quote(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo reset: emptied {Tables} tables before seeding")]
    private static partial void LogReset(ILogger logger, int tables);
}
