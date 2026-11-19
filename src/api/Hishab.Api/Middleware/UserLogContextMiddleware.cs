using Hishab.Application.Common.Security;
using Serilog;
using Serilog.Context;

namespace Hishab.Api.Middleware;

/// <summary>
/// After authentication, adds TenantId and UserId to every log line of the request and to
/// the request summary line. IDs only: never names or phone numbers.
/// </summary>
public sealed class UserLogContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IDiagnosticContext diagnosticContext)
    {
        var tenantId = context.User.FindFirst(HishabClaimTypes.TenantId)?.Value;
        var userId = context.User.FindFirst(HishabClaimTypes.UserId)?.Value;

        if (tenantId is not null)
        {
            diagnosticContext.Set("TenantId", tenantId);
        }

        if (userId is not null)
        {
            diagnosticContext.Set("UserId", userId);
        }

        using (LogContext.PushProperty("TenantId", tenantId))
        using (LogContext.PushProperty("UserId", userId))
        {
            await next(context);
        }
    }
}
