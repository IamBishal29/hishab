using Hishab.Api.RateLimiting;
using Hishab.Application.Shops.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hishab.Api.Controllers;

public sealed record RequestOtpRequest(string Phone);

public sealed record VerifyOtpRequest(string Phone, string Code);

/// <summary>Phone + OTP sign-up and login. Tokens (JWT) are added in #6.</summary>
[ApiController]
[Route("auth")]
[EnableRateLimiting(RateLimitingSetup.OtpPolicy)]
public sealed class AuthController(IOtpAuthService auth) : ControllerBase
{
    /// <summary>Sends a 6-digit code. Same response for every valid phone, registered or not.</summary>
    [HttpPost("otp")]
    [ProducesResponseType<OtpSent>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<OtpSent>> RequestOtp(RequestOtpRequest request, CancellationToken cancellationToken) =>
        Accepted(await auth.RequestOtpAsync(request.Phone, cancellationToken));

    /// <summary>Verifies the code; the first login of a phone creates its shop and Owner.</summary>
    [HttpPost("otp/verify")]
    [ProducesResponseType<OtpLogin>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<OtpLogin>> VerifyOtp(VerifyOtpRequest request, CancellationToken cancellationToken) =>
        Ok(await auth.VerifyOtpAsync(request.Phone, request.Code, cancellationToken));
}
