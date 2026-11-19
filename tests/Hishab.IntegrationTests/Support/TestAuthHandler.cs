using System.Security.Claims;
using System.Text.Encodings.Web;
using Hishab.Application.Common.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hishab.IntegrationTests.Support;

/// <summary>
/// Authenticates requests carrying <c>X-Test-User: {tenantId}|{userId}</c>. Stands in for
/// JWT (#6) so log-context and authorization behaviour can be tested now.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string Header = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var value = Request.Headers[Header].ToString();
        if (value.Split('|') is not [var tenantId, var userId])
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim(HishabClaimTypes.TenantId, tenantId), new Claim(HishabClaimTypes.UserId, userId)],
            SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
