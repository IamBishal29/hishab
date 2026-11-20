namespace Hishab.Domain.Common;

public enum AuditAction
{
    Created,
    Updated,
    Deleted,
}

/// <summary>
/// One recorded change: who changed which entity, how, and the field values before and after
/// (personal fields masked). Append-only: never updated or deleted by the application.
/// </summary>
public sealed class AuditEntry : ITenantOwned
{
    public const int EntityTypeMaxLength = 100;
    public const int EntityIdMaxLength = 100;
    public const int CorrelationIdMaxLength = 64;

    // For EF Core materialisation only.
    private AuditEntry()
    {
        EntityType = null!;
        EntityId = null!;
        Changes = null!;
    }

    private AuditEntry(Guid tenantId, Guid? userId, string entityType, string entityId, AuditAction action, string changes, string? correlationId, DateTimeOffset occurredAt)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        UserId = userId;
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        Changes = changes;
        CorrelationId = correlationId;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Database-assigned, increasing: the stable newest-first order. Timestamps tie (same
    /// millisecond, or one transaction) and random Guids have no order.
    /// </summary>
    public long Sequence { get; private set; }

    public Guid TenantId { get; set; }

    /// <summary>Who made the change; null for sign-up and background work. No foreign key: users can be removed, their history stays.</summary>
    public Guid? UserId { get; private set; }

    public string EntityType { get; private set; }

    public string EntityId { get; private set; }

    public AuditAction Action { get; private set; }

    /// <summary>JSON object: <c>{"field": {"old": ..., "new": ...}}</c>.</summary>
    public string Changes { get; private set; }

    /// <summary>Links the entry to the request's log lines.</summary>
    public string? CorrelationId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static AuditEntry Record(Guid tenantId, Guid? userId, string entityType, string entityId, AuditAction action, string changes, string? correlationId, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(changes);
        return new AuditEntry(tenantId, userId, entityType, entityId, action, changes, correlationId?[..Math.Min(correlationId.Length, CorrelationIdMaxLength)], occurredAt);
    }
}
