using Hishab.Domain.Couriers;

namespace Hishab.Application.Shops;

/// <summary>The current tenant's shop profile. There is no shop id parameter: the shop is always the caller's own.</summary>
public interface IShopProfileService
{
    Task<ShopProfile> GetAsync(CancellationToken cancellationToken);

    /// <exception cref="Domain.Common.DomainValidationException">A value breaks a shop rule.</exception>
    Task<ShopProfile> UpdateAsync(ShopProfileUpdate update, CancellationToken cancellationToken);
}

/// <summary>Amounts are in BDT.</summary>
public sealed record ShopProfile(
    Guid Id,
    string Name,
    string? LogoUrl,
    string? PickupAddress,
    CourierProvider? DefaultCourier,
    decimal DeliveryChargeInsideDhaka,
    decimal DeliveryChargeOutsideDhaka);

/// <summary>The editable fields only; amounts in BDT.</summary>
public sealed record ShopProfileUpdate(
    string Name,
    string? LogoUrl,
    string? PickupAddress,
    CourierProvider? DefaultCourier,
    decimal DeliveryChargeInsideDhaka,
    decimal DeliveryChargeOutsideDhaka);
