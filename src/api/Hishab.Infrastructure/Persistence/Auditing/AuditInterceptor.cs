using System.Text.Json;
using Hishab.Application.Common.Auditing;
using Hishab.Application.Common.Privacy;
using Hishab.Application.Common.Tenancy;
using Hishab.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Hishab.Infrastructure.Persistence.Auditing;

public enum PersonalDataKind
{
    /// <summary>Masked like logs: 01712****78.</summary>
    Phone,

    /// <summary>Replaced entirely; the entry still shows that the field changed.</summary>
    Text,
}

/// <summary>Mapping-level audit metadata, so entities (and inherited Identity properties) stay free of persistence attributes.</summary>
public static class AuditMapping
{
    internal const string PersonalAnnotation = "Hishab:Audit:Personal";
    internal const string IgnoreAnnotation = "Hishab:Audit:Ignore";
    internal const string MaskedText = "***";

    public static PropertyBuilder<T> IsPersonalData<T>(this PropertyBuilder<T> property, PersonalDataKind kind) =>
        property.HasAnnotation(PersonalAnnotation, kind);

    public static PropertyBuilder<T> IsNotAudited<T>(this PropertyBuilder<T> property) =>
        property.HasAnnotation(IgnoreAnnotation, true);
}

/// <summary>
/// Records every change to an <see cref="IAuditable"/> entity as an <see cref="AuditEntry"/>
/// in the same SaveChanges, so the change and its audit commit or roll back together.
/// Services do nothing; they cannot forget. Bulk ExecuteUpdate/ExecuteDelete bypass it, so
/// auditable entities must be changed through the change tracker.
/// </summary>
internal sealed class AuditInterceptor(ITenantContext tenant, IRequestContext request, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges(); // the interceptor runs before SaveChanges detects changes itself
        var entries = context.ChangeTracker.Entries().ToList();

        if (entries.Any(e => e.Entity is AuditEntry && e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit entries are append-only.");
        }

        var now = clock.GetUtcNow();
        foreach (var entry in entries.Where(e => e.Entity is IAuditable && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var changes = AuditChanges.Describe(entry);
            if (changes is null)
            {
                continue; // e.g. only ignored fields changed
            }

            var tenantId = tenant.TenantId
                ?? (entry.Entity as ITenantOwned)?.TenantId
                ?? throw new InvalidOperationException($"Cannot audit {entry.Metadata.ClrType.Name}: no tenant is set.");

            context.Add(AuditEntry.Record(
                tenantId,
                request.UserId,
                entry.Metadata.ClrType.Name,
                KeyOf(entry),
                ActionOf(entry.State),
                changes,
                request.CorrelationId,
                now));
        }
    }

    private static string KeyOf(EntityEntry entry) =>
        string.Join(",", entry.Metadata.FindPrimaryKey()!.Properties.Select(p => entry.Property(p.Name).CurrentValue));

    private static AuditAction ActionOf(EntityState state) => state switch
    {
        EntityState.Added => AuditAction.Created,
        EntityState.Deleted => AuditAction.Deleted,
        _ => AuditAction.Updated,
    };
}

/// <summary>Builds the <c>{"field": {"old": ..., "new": ...}}</c> diff with masking and secret filtering.</summary>
internal static class AuditChanges
{
    private static readonly string[] _secretNameParts = ["Hash", "Stamp", "Secret", "Token"];
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public static string? Describe(EntityEntry entry)
    {
        var changes = new Dictionary<string, FieldChange>();
        foreach (var property in entry.Properties)
        {
            var meta = property.Metadata;
            if (!IsRecorded(meta))
            {
                continue;
            }

            var oldValue = entry.State == EntityState.Added ? null : Present(meta, property.OriginalValue);
            var newValue = entry.State == EntityState.Deleted ? null : Present(meta, property.CurrentValue);

            if (entry.State == EntityState.Modified && !property.IsModified)
            {
                continue;
            }

            if (Equals(property.OriginalValue, property.CurrentValue) && entry.State == EntityState.Modified)
            {
                continue; // set to the same value
            }

            if (oldValue is null && newValue is null)
            {
                continue;
            }

            changes[JsonNamingPolicy.CamelCase.ConvertName(meta.Name)] = new FieldChange(oldValue, newValue);
        }

        return changes.Count == 0 ? null : JsonSerializer.Serialize(changes, _json);
    }

    /// <summary>
    /// Keys, the tenant and concurrency tokens add nothing; secrets are never written, whether or
    /// not someone remembered the annotation (the name rule is the safety net).
    /// </summary>
    internal static bool IsRecorded(IProperty meta) =>
        !meta.IsPrimaryKey()
        && meta.Name != nameof(ITenantOwned.TenantId)
        && !meta.IsConcurrencyToken
        && meta.FindAnnotation(AuditMapping.IgnoreAnnotation)?.Value is not true
        && !_secretNameParts.Any(part => meta.Name.Contains(part, StringComparison.OrdinalIgnoreCase));

    /// <summary>The value as stored (Money as a number, enums as text), masked when personal.</summary>
    internal static object? Present(IProperty meta, object? value)
    {
        if (value is null)
        {
            return null;
        }

        var converter = meta.GetValueConverter() ?? meta.GetTypeMapping().Converter;
        var stored = converter is null ? value : converter.ConvertToProvider(value);

        return meta.FindAnnotation(AuditMapping.PersonalAnnotation)?.Value switch
        {
            PersonalDataKind.Phone => PhoneMasker.Mask(stored?.ToString()),
            PersonalDataKind.Text => AuditMapping.MaskedText,
            _ => stored,
        };
    }

    private sealed record FieldChange(object? Old, object? New);
}
