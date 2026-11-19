namespace Hishab.Application.Common.Security;

/// <summary>JWT claim names issued by Hishab (inbound claim mapping is turned off, so names stay short).</summary>
public static class HishabClaimTypes
{
    public const string UserId = "sub";

    public const string TenantId = "tenant_id";

    public const string Role = "role";
}
