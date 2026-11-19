using Hishab.Application.Common.Tenancy;

namespace Hishab.Infrastructure.Persistence.Tenancy;

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }

    public void SetTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id cannot be empty.", nameof(tenantId));
        }

        if (TenantId is { } current && current != tenantId)
        {
            throw new InvalidOperationException("The tenant for this scope is already set and cannot be changed.");
        }

        TenantId = tenantId;
    }
}
