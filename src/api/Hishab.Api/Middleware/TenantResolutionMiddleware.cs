using Hishab.Application.Common.Security;
using Hishab.Application.Common.Tenancy;

namespace Hishab.Api.Middleware;

/// <summary>
/// After authentication, sets the request's tenant from the signed <c>tenant_id</c> claim,
/// the only trusted source. No claim (anonymous or malformed) leaves the tenant unset, so
/// tenant-owned queries return nothing (fail closed).
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, ITenantContext tenant)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirst(HishabClaimTypes.TenantId)?.Value, out var tenantId)
            && tenantId != Guid.Empty)
        {
            tenant.SetTenant(tenantId);
        }

        return next(context);
    }
}
