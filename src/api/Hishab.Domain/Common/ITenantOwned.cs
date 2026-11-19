namespace Hishab.Domain.Common;

/// <summary>
/// Marks an entity that belongs to exactly one shop (tenant). Persistence filters every
/// query by the current tenant and stamps <see cref="TenantId"/> on insert.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
