namespace Hishab.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Npgsql connection string. Comes from user-secrets or environment variables, never appsettings.json.</summary>
    public string ConnectionString { get; set; } = string.Empty;
}
