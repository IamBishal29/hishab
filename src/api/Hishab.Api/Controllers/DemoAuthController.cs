using FluentValidation;
using Hishab.Api.RateLimiting;
using Hishab.Api.Routing;
using Hishab.Application.Shops.Auth;
using Hishab.Domain.Shops;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hishab.Api.Controllers;

/// <summary>Who to sign in as. Owner when left out.</summary>
public sealed record DemoLoginRequest(ShopRole Role = ShopRole.Owner);

public sealed class DemoLoginRequestValidator : AbstractValidator<DemoLoginRequest>
{
    public DemoLoginRequestValidator() => RuleFor(r => r.Role).IsInEnum();
}

/// <summary>The roles a visitor can try; the web shows the demo button only when this answers 200.</summary>
public sealed record DemoInfo(IReadOnlyList<ShopRole> Roles);

/// <summary>
/// One-click sign-in for the hosted demo. Two locks: the route exists only in Development/Demo
/// (<see cref="DevOnlyControllersConvention"/>), and even there it answers 404 until <c>Demo:Login</c> is on.
/// </summary>
[ApiController]
[DevOnly]
[Route("auth/demo")]
[ApiExplorerSettings(IgnoreApi = true)] // not part of the product's API contract
public sealed class DemoAuthController(IDemoLoginService demo, IAuthTokenService tokens) : ControllerBase
{
    [HttpGet]
    public ActionResult<DemoInfo> Get() => Ok(new DemoInfo(demo.AvailableRoles()));

    /// <summary>Signs in like a verified OTP; the refresh token goes only into the HttpOnly cookie.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitingSetup.OtpPolicy)]
    public async Task<ActionResult<LoginResponse>> SignIn(DemoLoginRequest request, CancellationToken cancellationToken)
    {
        var login = await demo.SignInAsync(request.Role, cancellationToken);
        var issued = await tokens.IssueAsync(login.UserId, login.ShopId, login.Role, cancellationToken);
        Response.Cookies.Append(AuthController.RefreshCookie, issued.RefreshToken, AuthController.CookieOptions(issued.RefreshTokenExpiresAt));
        return Ok(new LoginResponse(
            login.UserId, login.ShopId, login.Role, IsNewShop: false,
            issued.AccessToken, issued.AccessTokenExpiresAt, RefreshToken: null, issued.RefreshTokenExpiresAt));
    }
}
