using Hishab.Application.Common.Auditing;
using Hishab.Application.Common.Security;

namespace Hishab.Api.Auth;

/// <summary>The acting user (signed <c>sub</c> claim) and correlation ID of the current HTTP request.</summary>
public sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    public Guid? UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(HishabClaimTypes.UserId)?.Value, out var id) ? id : null;

    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;
}
