using Hishab.Application.Shops;
using Hishab.Domain.Common;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hishab.Infrastructure.Shops;

internal sealed class ShopProfileService(HishabDbContext db) : IShopProfileService
{
    public async Task<ShopProfile> GetAsync(CancellationToken cancellationToken) =>
        ToProfile(await CurrentShopAsync(cancellationToken));

    public async Task<ShopProfile> UpdateAsync(ShopProfileUpdate update, CancellationToken cancellationToken)
    {
        var shop = await CurrentShopAsync(cancellationToken);
        shop.UpdateProfile(
            update.Name,
            update.LogoUrl,
            update.PickupAddress,
            update.DefaultCourier,
            Money.Bdt(update.DeliveryChargeInsideDhaka, "deliveryChargeInsideDhaka"),
            Money.Bdt(update.DeliveryChargeOutsideDhaka, "deliveryChargeOutsideDhaka"));
        await db.SaveChangesAsync(cancellationToken);
        return ToProfile(shop);
    }

    /// <summary>The tenant filter on shops returns only the caller's shop, so no id is needed (or accepted).</summary>
    private async Task<Shop> CurrentShopAsync(CancellationToken cancellationToken) =>
        await db.Shops.SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The signed-in tenant has no shop."); // a bug, not a user error

    private static ShopProfile ToProfile(Shop shop) => new(
        shop.Id,
        shop.Name,
        shop.LogoUrl,
        shop.PickupAddress,
        shop.DefaultCourier,
        shop.DeliveryChargeInsideDhaka.Amount,
        shop.DeliveryChargeOutsideDhaka.Amount);
}
