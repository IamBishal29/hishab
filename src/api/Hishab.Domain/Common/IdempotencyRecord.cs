namespace Hishab.Domain.Common;

/// <summary>
/// Remembers a client's idempotency key: which operation it was used for, a fingerprint of the
/// request (a hash, so no personal data is stored) and the resource it produced. A retry with
/// the same key and fingerprint gets that resource back; a reuse with different content is refused.
/// </summary>
public sealed class IdempotencyRecord : ITenantOwned
{
    public const int KeyMaxLength = 100;
    public const int OperationMaxLength = 50;

    private IdempotencyRecord()
    {
        Key = null!;
        Operation = null!;
        RequestHash = null!;
    }

    public IdempotencyRecord(string key, string operation, string requestHash, Guid resourceId, DateTimeOffset createdAt)
    {
        Key = key;
        Operation = operation;
        RequestHash = requestHash;
        ResourceId = resourceId;
        CreatedAt = createdAt;
    }

    public Guid TenantId { get; set; }

    public string Key { get; private set; }

    public string Operation { get; private set; }

    /// <summary>SHA-256 (hex) of the normalised request.</summary>
    public string RequestHash { get; private set; }

    public Guid ResourceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
