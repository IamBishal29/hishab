namespace Hishab.Application.Common.Tenancy;

/// <summary>
/// The tenant the current unit of work runs for. Set from the authenticated user's
/// <c>tenant_id</c> claim in HTTP requests, or explicitly by background jobs.
/// Never set from request bodies, query strings or URLs.
/// </summary>
public interface ITenantContext
{
    /// <summary>Null until a tenant is set; data access then sees no tenant-owned rows.</summary>
    Guid? TenantId { get; }

    /// <summary>Sets the tenant once per scope. Switching to a different tenant throws.</summary>
    void SetTenant(Guid tenantId);
}
