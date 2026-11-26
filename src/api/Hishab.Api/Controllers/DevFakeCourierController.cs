using Hishab.Api.Auth;
using Hishab.Api.Routing;
using Hishab.Application.Orders;
using Hishab.Domain.Couriers;
using Hishab.Infrastructure.Couriers.Fake;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hishab.Api.Controllers;

/// <summary>Where the Fake courier's parcel should be now.</summary>
public sealed class FakeParcelMoveRequest
{
    /// <summary>InTransit, Delivered or Returned (one step at a time, as a real courier reports).</summary>
    /// <example>Delivered</example>
    public required ParcelStatus Status { get; init; }
}

/// <summary>
/// Development and Demo only (removed elsewhere by <see cref="DevOnlyControllersConvention"/>): plays the Fake
/// courier for demos and the end-to-end test. The order then moves through the normal status sync.
/// </summary>
[ApiController]
[DevOnly]
[Route("dev/fake-courier")]
[Authorize(Policy = Policies.CanPack)]
[ApiExplorerSettings(IgnoreApi = true)] // not part of the product's API contract
public sealed class DevFakeCourierController(FakeCourierSimulator simulator) : ControllerBase
{
    [HttpPost("orders/{id:guid}/status")]
    public async Task<ActionResult<OrderDetails>> Move(Guid id, FakeParcelMoveRequest request, CancellationToken cancellationToken) =>
        Ok(await simulator.MoveAsync(id, request.Status, cancellationToken));
}
