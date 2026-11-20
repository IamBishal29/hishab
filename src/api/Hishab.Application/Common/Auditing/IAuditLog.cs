using System.Text.Json;
using Hishab.Domain.Common;

namespace Hishab.Application.Common.Auditing;

/// <summary>Reads the current shop's audit log.</summary>
public interface IAuditLog
{
    /// <summary>Newest first. <paramref name="page"/> starts at 1.</summary>
    Task<AuditPage> ListAsync(int page, int pageSize, CancellationToken cancellationToken);
}

public sealed record AuditPage(IReadOnlyList<AuditRecord> Items, int Page, int PageSize, int TotalCount);

/// <summary>One change. <see cref="Changes"/> is <c>{"field": {"old": ..., "new": ...}}</c> with personal data masked.</summary>
public sealed record AuditRecord(
    Guid Id,
    Guid? UserId,
    string EntityType,
    string EntityId,
    AuditAction Action,
    JsonElement Changes,
    string? CorrelationId,
    DateTimeOffset OccurredAt);
