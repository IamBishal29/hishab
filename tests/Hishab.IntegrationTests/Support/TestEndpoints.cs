using Hishab.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Hishab.IntegrationTests.Support;

/// <summary>Test-only endpoints, added to the app by <see cref="HishabApiFactory"/> only.</summary>
[ApiController]
[Route("_test")]
public sealed partial class TestEndpointsController(ILogger<TestEndpointsController> logger) : ControllerBase
{
    public const string SecretMessage = "connection string Host=db;Password=hunter2";

    [HttpGet("throw/{kind}")]
    public IActionResult Throw(string kind) => kind switch
    {
        "not-found" => throw new NotFoundException("Order", 42),
        "conflict" => throw new ConflictException("Order 42 is already booked."),
        "validation" => throw new DomainValidationException("phone", "Phone number is not a Bangladeshi mobile number."),
        "boom" => throw new InvalidOperationException(SecretMessage),
        _ => NotFound(),
    };

    [HttpGet("log-phone")]
    public IActionResult LogPhone()
    {
        LogCustomerCall(logger, "01712345678");
        return Ok();
    }

    [HttpGet("ok")]
    public IActionResult Ok200() => Ok();

    // Deliberately logs a raw phone: the redaction enricher must catch it.
    [LoggerMessage(Level = LogLevel.Information, Message = "Calling customer {Phone}")]
    private static partial void LogCustomerCall(ILogger logger, string phone);
}

/// <summary>One endpoint per policy, so authorization can be tested before the real features exist.</summary>
[ApiController]
[Route("_test/policy")]
public sealed class PolicyProbeController : ControllerBase
{
    [HttpGet("manage-shop")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = Hishab.Api.Auth.Policies.CanManageShop)]
    public IActionResult ManageShop() => Ok();

    [HttpGet("manage-staff")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = Hishab.Api.Auth.Policies.CanManageStaff)]
    public IActionResult ManageStaff() => Ok();

    [HttpGet("enter-orders")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = Hishab.Api.Auth.Policies.CanEnterOrders)]
    public IActionResult EnterOrders() => Ok();

    [HttpGet("pack")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = Hishab.Api.Auth.Policies.CanPack)]
    public IActionResult Pack() => Ok();

    [HttpGet("see-profit")]
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = Hishab.Api.Auth.Policies.CanSeeProfit)]
    public IActionResult SeeProfit() => Ok();
}
