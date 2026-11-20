using FluentValidation;
using Hishab.Api.Auth;
using Hishab.Application.Shops;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Shops;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hishab.Api.Controllers;

/// <summary>
/// The editable shop profile. Only these fields bind from JSON: the id, tenant and creation
/// time can never be posted (no over-posting), because the entity is never bound.
/// </summary>
public sealed class UpdateShopRequest
{
    /// <example>Rahim Fashion</example>
    public string Name { get; init; } = string.Empty;

    /// <summary>Absolute http(s) URL of the logo image.</summary>
    /// <example>https://example.com/rahim-fashion-logo.png</example>
    public string? LogoUrl { get; init; }

    /// <summary>Where couriers collect parcels.</summary>
    /// <example>House 12, Road 5, Mirpur 10, Dhaka 1216</example>
    public string? PickupAddress { get; init; }

    /// <summary>Courier pre-selected when booking. Null for none.</summary>
    /// <example>Steadfast</example>
    public CourierProvider? DefaultCourier { get; init; }

    /// <summary>BDT, up to 2 decimal places.</summary>
    /// <example>60</example>
    public decimal DeliveryChargeInsideDhaka { get; init; }

    /// <summary>BDT, up to 2 decimal places.</summary>
    /// <example>120</example>
    public decimal DeliveryChargeOutsideDhaka { get; init; }
}

public sealed class UpdateShopRequestValidator : AbstractValidator<UpdateShopRequest>
{
    /// <summary>Sanity cap: a typo such as 6000 instead of 60 must not reach every order.</summary>
    public const decimal MaxDeliveryCharge = 5_000m;

    public UpdateShopRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(Shop.NameMaxLength);
        RuleFor(r => r.LogoUrl!)
            .MaximumLength(Shop.LogoUrlMaxLength)
            .Must(Shop.IsHttpUrl).WithMessage("Logo must be an http or https URL.")
            .When(r => !string.IsNullOrWhiteSpace(r.LogoUrl));
        RuleFor(r => r.PickupAddress).MaximumLength(Shop.PickupAddressMaxLength);
        RuleFor(r => r.DefaultCourier).IsInEnum();
        RuleFor(r => r.DeliveryChargeInsideDhaka).SetValidator(new DeliveryChargeValidator());
        RuleFor(r => r.DeliveryChargeOutsideDhaka).SetValidator(new DeliveryChargeValidator());
    }

    private sealed class DeliveryChargeValidator : AbstractValidator<decimal>
    {
        public DeliveryChargeValidator() =>
            RuleFor(amount => amount)
                .InclusiveBetween(0m, MaxDeliveryCharge).WithMessage($"Delivery charge must be between 0 and {MaxDeliveryCharge:0} BDT.")
                .Must(Money.HasValidScale).WithMessage($"Delivery charge can have at most {Money.MaxDecimals} decimal places.");
    }
}

/// <summary>The shop profile. Amounts are in <see cref="Currency"/>.</summary>
public sealed record ShopResponse(
    Guid Id,
    string Name,
    string? LogoUrl,
    string? PickupAddress,
    CourierProvider? DefaultCourier,
    decimal DeliveryChargeInsideDhaka,
    decimal DeliveryChargeOutsideDhaka)
{
    /// <example>BDT</example>
    public string Currency => Money.Currency;

    internal static ShopResponse From(ShopProfile p) => new(
        p.Id, p.Name, p.LogoUrl, p.PickupAddress, p.DefaultCourier, p.DeliveryChargeInsideDhaka, p.DeliveryChargeOutsideDhaka);
}

/// <summary>The signed-in user's own shop. There is no shop id in the URL: the shop comes from the token.</summary>
[ApiController]
[Route("shop")]
[Authorize(Policy = Policies.ManageShop)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class ShopController(IShopProfileService shops) : ControllerBase
{
    /// <summary>Gets the shop profile and delivery charges.</summary>
    [HttpGet]
    [ProducesResponseType<ShopResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ShopResponse>> Get(CancellationToken cancellationToken) =>
        Ok(ShopResponse.From(await shops.GetAsync(cancellationToken)));

    /// <summary>Replaces the shop profile and delivery charges (Owner or Manager).</summary>
    [HttpPut]
    [ProducesResponseType<ShopResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ShopResponse>> Update(UpdateShopRequest request, CancellationToken cancellationToken) =>
        Ok(ShopResponse.From(await shops.UpdateAsync(
            new ShopProfileUpdate(
                request.Name,
                request.LogoUrl,
                request.PickupAddress,
                request.DefaultCourier,
                request.DeliveryChargeInsideDhaka,
                request.DeliveryChargeOutsideDhaka),
            cancellationToken)));
}
