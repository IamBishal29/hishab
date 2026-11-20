using Hishab.Api.RateLimiting;
using Hishab.Application.Common.Security;
using Hishab.Application.Common.Tenancy;
using Hishab.Application.Shops.Auth;
using Hishab.Domain.Common;
using Hishab.Domain.Shops;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace Hishab.Api.Controllers;

/// <summary>Where the refresh token goes. Browsers use the HttpOnly cookie; other clients ask for the body.</summary>
public enum TokenDelivery
{
    Cookie,
    Body,
}

public sealed record RequestOtpRequest(string Phone);

public sealed record VerifyOtpRequest(string Phone, string Code, TokenDelivery TokenDelivery = TokenDelivery.Cookie);

/// <summary>Leave <see cref="RefreshToken"/> empty to use the cookie.</summary>
public sealed record RefreshTokenRequest(string? RefreshToken);

public sealed record LoginResponse(
    Guid UserId,
    Guid ShopId,
    ShopRole Role,
    bool IsNewShop,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string? RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string? RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record MeResponse(Guid UserId, Guid ShopId, string Role);

/// <summary>Phone + OTP sign-up and login, token refresh and logout.</summary>
[ApiController]
[Route("auth")]
public sealed class AuthController(IOtpAuthService auth, IAuthTokenService tokens) : ControllerBase
{
    public const string RefreshCookie = "hishab_rt";

    /// <summary>Sends a 6-digit code. Same response for every valid phone, registered or not.</summary>
    [HttpPost("otp")]
    [EnableRateLimiting(RateLimitingSetup.OtpPolicy)]
    [ProducesResponseType<OtpSent>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<OtpSent>> RequestOtp(RequestOtpRequest request, CancellationToken cancellationToken) =>
        Accepted(await auth.RequestOtpAsync(request.Phone, cancellationToken));

    /// <summary>Verifies the code and signs in; the first login of a phone creates its shop and Owner.</summary>
    [HttpPost("otp/verify")]
    [EnableRateLimiting(RateLimitingSetup.OtpPolicy)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginResponse>> VerifyOtp(VerifyOtpRequest request, CancellationToken cancellationToken)
    {
        var login = await auth.VerifyOtpAsync(request.Phone, request.Code, cancellationToken);
        var issued = await tokens.IssueAsync(login.UserId, login.ShopId, login.Role, cancellationToken);
        var refreshToken = Deliver(issued, request.TokenDelivery);
        return Ok(new LoginResponse(
            login.UserId, login.ShopId, login.Role, login.IsNewShop,
            issued.AccessToken, issued.AccessTokenExpiresAt, refreshToken, issued.RefreshTokenExpiresAt));
    }

    /// <summary>
    /// Exchanges a refresh token (body, else cookie) for a new pair, answered the same way it
    /// came in. Each refresh token works once; replaying one signs out every session of that login.
    /// </summary>
    [HttpPost("refresh")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TokenResponse>> Refresh(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshTokenRequest? request,
        CancellationToken cancellationToken)
    {
        var (presented, delivery) = PresentedToken(request);
        if (presented is null)
        {
            throw new AuthenticationFailedException(AuthTokenMessages.SessionExpired);
        }

        AuthTokens issued;
        try
        {
            issued = await tokens.RefreshAsync(presented, cancellationToken);
        }
        catch (AuthenticationFailedException)
        {
            Response.Cookies.Delete(RefreshCookie, CookieOptions(expires: null));
            throw;
        }

        var refreshToken = Deliver(issued, delivery);
        return Ok(new TokenResponse(issued.AccessToken, issued.AccessTokenExpiresAt, refreshToken, issued.RefreshTokenExpiresAt));
    }

    /// <summary>Signs out this login on every device that shares it. Always succeeds.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshTokenRequest? request,
        CancellationToken cancellationToken)
    {
        if (PresentedToken(request).Token is { } presented)
        {
            await tokens.RevokeFamilyAsync(presented, cancellationToken);
        }

        Response.Cookies.Delete(RefreshCookie, CookieOptions(expires: null));
        return NoContent();
    }

    /// <summary>The signed-in user, as read from the access token.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public ActionResult<MeResponse> Me([FromServices] ITenantContext tenant) => Ok(new MeResponse(
        Guid.Parse(User.FindFirst(HishabClaimTypes.UserId)!.Value),
        tenant.TenantId ?? throw new InvalidOperationException("Authenticated request without a tenant."),
        User.FindFirst(HishabClaimTypes.Role)!.Value));

    private (string? Token, TokenDelivery Delivery) PresentedToken(RefreshTokenRequest? request) =>
        !string.IsNullOrWhiteSpace(request?.RefreshToken)
            ? (request.RefreshToken, TokenDelivery.Body)
            : (Request.Cookies[RefreshCookie], TokenDelivery.Cookie);

    /// <summary>Puts the refresh token in exactly one place: the body for API clients, or an HttpOnly cookie that scripts (and XSS) cannot read.</summary>
    private string? Deliver(AuthTokens issued, TokenDelivery delivery)
    {
        if (delivery == TokenDelivery.Body)
        {
            return issued.RefreshToken;
        }

        Response.Cookies.Append(RefreshCookie, issued.RefreshToken, CookieOptions(issued.RefreshTokenExpiresAt));
        return null;
    }

    // Path=/api/auth: the cookie travels only to refresh and logout, never with ordinary API calls.
    // SameSite=Strict: other sites cannot make the browser send it (CSRF).
    private static CookieOptions CookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = expires,
        IsEssential = true,
    };
}
