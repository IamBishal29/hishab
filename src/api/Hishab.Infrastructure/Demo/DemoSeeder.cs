using System.Security.Claims;
using Hishab.Application.Common.Security;
using Hishab.Application.Common.Tenancy;
using Hishab.Application.Couriers;
using Hishab.Application.Finance;
using Hishab.Application.Orders;
using Hishab.Application.Products;
using Hishab.Application.Shops;
using Hishab.Application.Shops.Staff;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Finance;
using Hishab.Domain.Orders;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Couriers;
using Hishab.Infrastructure.Couriers.Fake;
using Hishab.Infrastructure.Identity;
using Hishab.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hishab.Infrastructure.Demo;

/// <summary>What one run did: zero steps on a second run means the demo shop was already complete.</summary>
public sealed record DemoSeedSummary(Guid ShopId, int StepsApplied);

/// <summary>
/// Builds the demo shop through the same services a seller's clicks use (orders, booking with the Fake
/// courier, courier status sync, payouts), signed in as the demo owner. Every rule (order numbers,
/// lifecycle, snapshots, audit) therefore holds for demo data too. Convergent: each step checks whether
/// it already happened, so running it again, or after a half-finished run, never duplicates anything.
/// </summary>
public sealed partial class DemoSeeder(
    IServiceScopeFactory scopes,
    FakeCourierControl fakeCourier,
    TimeProvider time,
    ILogger<DemoSeeder> logger,
    IHttpContextAccessor? httpContext = null)
{
    private readonly SeedClock? _clock = time as SeedClock;

    public async Task<DemoSeedSummary> SeedAsync(CancellationToken cancellationToken)
    {
        var now = _clock?.RealNow ?? time.GetUtcNow();
        var timeline = DemoScript.Timeline(now);
        var previousContext = httpContext?.HttpContext;
        try
        {
            TravelTo(timeline[0].At.AddDays(-1)); // the shop opened the day before its first order
            var (shopId, ownerId) = await EnsureShopAsync(cancellationToken);

            // Act as the owner, exactly as a signed-in request would (permissions, audit "changed by").
            if (httpContext is not null)
            {
                httpContext.HttpContext = OwnerRequest(shopId, ownerId);
            }

            var steps = await InShopAsync(shopId, sp => EnsureSetupAsync(sp, cancellationToken));
            var variants = await InShopAsync(shopId, sp => VariantsBySkuAsync(sp, cancellationToken));
            var orderIds = new Dictionary<int, Guid>();

            foreach (var step in timeline)
            {
                TravelTo(step.At);
                if (await InShopAsync(shopId, sp => ApplyAsync(sp, shopId, step, variants, orderIds, cancellationToken)))
                {
                    steps++;
                }
            }

            LogSeeded(logger, shopId, steps);
            return new DemoSeedSummary(shopId, steps);
        }
        finally
        {
            _clock?.ReturnToPresent();
            if (httpContext is not null)
            {
                httpContext.HttpContext = previousContext;
            }
        }
    }

    private void TravelTo(DateTimeOffset at) => _clock?.TravelTo(at);

    private async Task<T> InShopAsync<T>(Guid shopId, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(shopId);
        return await work(scope.ServiceProvider);
    }

    /// <summary>Finds the demo owner across shops (like the login lookup), or creates shop and owner as sign-up does.</summary>
    private async Task<(Guid ShopId, Guid OwnerId)> EnsureShopAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HishabDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<HishabUser>>();
        var phone = DemoScript.OwnerPhone.Value;
        var normalized = users.NormalizeName(phone);

        var existing = await db.Users
            .IgnoreQueryFilters([HishabDbContext.TenantFilter])
            .SingleOrDefaultAsync(u => u.NormalizedUserName == normalized, cancellationToken);
        if (existing is not null)
        {
            return (existing.TenantId, existing.Id);
        }

        var now = time.GetUtcNow();
        var shop = Shop.Create(now);
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(shop.Id);
        db.Shops.Add(shop);
        var owner = new HishabUser
        {
            Id = Guid.NewGuid(),
            UserName = phone,
            PhoneNumber = phone,
            PhoneNumberConfirmed = true,
            Role = ShopRole.Owner,
            CreatedAt = now,
        };
        var created = await users.CreateAsync(owner); // saves shop and owner together
        if (!created.Succeeded)
        {
            throw new InvalidOperationException("Creating the demo owner failed: " + string.Join(", ", created.Errors.Select(e => e.Code)));
        }

        return (shop.Id, owner.Id);
    }

    private static DefaultHttpContext OwnerRequest(Guid shopId, Guid ownerId) => new()
    {
        TraceIdentifier = "demo-seed", // the audit log's correlation ID for everything seeded
        User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(HishabClaimTypes.UserId, ownerId.ToString()),
            new Claim(HishabClaimTypes.TenantId, shopId.ToString()),
            new Claim(HishabClaimTypes.Role, nameof(ShopRole.Owner)),
        ], authenticationType: "demo-seed")),
    };

    /// <summary>Plan, courier, profile, staff and catalogue: each added only if missing.</summary>
    private static async Task<int> EnsureSetupAsync(IServiceProvider sp, CancellationToken cancellationToken)
    {
        var steps = 0;
        var db = sp.GetRequiredService<HishabDbContext>();
        var shop = await db.Shops.SingleAsync(cancellationToken);
        if (shop.Plan != Plan.Growth)
        {
            shop.ChangePlan(Plan.Growth); // room for three staff; billing would do this in real life
            await db.SaveChangesAsync(cancellationToken);
            steps++;
        }

        var couriers = sp.GetRequiredService<ICourierAccountService>();
        if (!(await couriers.ListAsync(cancellationToken)).Any(c => c.Provider == CourierProvider.Fake && c.IsConnected))
        {
            await couriers.ConnectAsync(CourierProvider.Fake, new Dictionary<string, string> { ["apiKey"] = DemoScript.CourierApiKey }, cancellationToken);
            steps++;
        }

        if (shop.Name != DemoScript.ShopName)
        {
            await sp.GetRequiredService<IShopProfileService>().UpdateAsync(
                new ShopProfileUpdate(DemoScript.ShopName, null, DemoScript.PickupAddress, CourierProvider.Fake, 60m, 120m),
                cancellationToken);
            steps++;
        }

        var staff = sp.GetRequiredService<IStaffService>();
        var members = (await staff.ListAsync(cancellationToken)).Select(m => Phone.Parse(m.Phone)).ToHashSet();
        foreach (var (phone, role) in DemoScript.Staff.Where(s => !members.Contains(s.Phone)))
        {
            await staff.InviteAsync(phone.Value, role, cancellationToken);
            steps++;
        }

        var products = sp.GetRequiredService<IProductService>();
        var skus = (await products.ListAsync(null, 1, 100, cancellationToken)).Items
            .SelectMany(p => p.Variants).Select(v => v.Sku).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var product in DemoScript.Products.Where(p => !p.Variants.Any(v => skus.Contains(v.Sku))))
        {
            await products.CreateAsync(product, cancellationToken);
            steps++;
        }

        return steps;
    }

    private static async Task<IReadOnlyDictionary<string, Guid>> VariantsBySkuAsync(IServiceProvider sp, CancellationToken cancellationToken) =>
        (await sp.GetRequiredService<IProductService>().ListAsync(null, 1, 100, cancellationToken)).Items
            .SelectMany(p => p.Variants)
            .ToDictionary(v => v.Sku, v => v.Id, StringComparer.OrdinalIgnoreCase);

    /// <returns>True when the step changed something; false when it had already happened.</returns>
    private async Task<bool> ApplyAsync(
        IServiceProvider sp, Guid shopId, DemoEvent step, IReadOnlyDictionary<string, Guid> variants,
        Dictionary<int, Guid> orderIds, CancellationToken cancellationToken)
    {
        var orders = sp.GetRequiredService<IOrderService>();
        var shipments = sp.GetRequiredService<IShipmentService>();

        if (step.Step == DemoStep.Payout)
        {
            return await PayAsync(sp, step.Payout!, orderIds, cancellationToken);
        }

        var order = step.Order!;
        if (step.Step == DemoStep.Create)
        {
            var customer = DemoScript.CustomerOf(order);
            var result = await orders.CreateAsync(
                new CreateOrderCommand(
                    customer.Phone.Value, customer.Name, customer.Address, customer.IsInsideDhaka, order.Source,
                    [.. order.Lines.Select(l => new OrderLineInput(variants[l.Sku], l.Quantity))],
                    DeliveryCharge: null, order.Advance, order.Note),
                order.IdempotencyKey, // a replay on later runs: same order back, nothing new
                cancellationToken);
            orderIds[order.No] = result.Details.Order.Id;
            return !result.Replayed;
        }

        var id = orderIds[order.No];
        var details = await orders.GetAsync(id, cancellationToken);
        var target = TargetOf(step.Step);
        if (details.Order.History.Any(h => h.To == target))
        {
            return false; // done on an earlier run
        }

        switch (step.Step)
        {
            case DemoStep.Confirm or DemoStep.Cancel:
                await orders.TransitionAsync(id, target, order.Note, cancellationToken);
                break;
            case DemoStep.Book:
                await shipments.BookAsync(id, CourierProvider.Fake, null, cancellationToken);
                break;
            case DemoStep.ReceiveBack:
                await shipments.ReceiveBackAsync(id, Money.Bdt(DemoScript.ReturnCharge), null, cancellationToken);
                break;
            default: // the courier moves the parcel; our status sync picks it up, as in production
                fakeCourier.SetStatus(details.Shipment!.ConsignmentId!, ParcelOf(step.Step), step.At);
                await SyncAsCourierAsync(shopId, cancellationToken);
                break;
        }

        return true;
    }

    private static async Task<bool> PayAsync(IServiceProvider sp, DemoPayout payout, Dictionary<int, Guid> orderIds, CancellationToken cancellationToken)
    {
        var payouts = sp.GetRequiredService<IPayoutService>();
        if ((await payouts.ListAsync(CourierProvider.Fake, 1, 100, cancellationToken)).Items.Any(p => p.Reference == payout.Reference))
        {
            return false;
        }

        var orders = sp.GetRequiredService<IOrderService>();
        var lines = new List<PayoutLineInput>();
        foreach (var order in payout.Orders)
        {
            var details = await orders.GetAsync(orderIds[order.No], cancellationToken);
            var cod = details.Order.CodAmount;
            lines.Add(new PayoutLineInput(
                details.Shipment!.ConsignmentId!,
                Money.Bdt(cod - order.ShortPaidBy),
                Money.Bdt(details.Shipment.DeliveryFee ?? 0m),
                Money.Bdt(Math.Round(cod * 0.01m, 2, MidpointRounding.AwayFromZero))));
        }

        var net = lines.Sum(l => l.Collected.Amount - l.DeliveryFee.Amount - l.CodFee.Amount);
        await payouts.RecordAsync(
            new RecordPayout(CourierProvider.Fake, payout.Reference, payout.PaidOn, Money.Bdt(net), "Weekly COD payout", lines),
            cancellationToken);
        return true;
    }

    /// <summary>Courier moves are system work, as in the background job: run without the owner's request.</summary>
    private async Task SyncAsCourierAsync(Guid shopId, CancellationToken cancellationToken)
    {
        Task sync;
        using (ExecutionContext.SuppressFlow())
        {
            sync = Task.Run(() => InShopAsync(shopId, async sp =>
                await sp.GetRequiredService<CourierStatusSync>().SyncAsync(CourierProvider.Fake, cancellationToken)), cancellationToken);
        }

        await sync;
    }

    private static OrderStatus TargetOf(DemoStep step) => step switch
    {
        DemoStep.Confirm => OrderStatus.Confirmed,
        DemoStep.Cancel => OrderStatus.Cancelled,
        DemoStep.Book => OrderStatus.Booked,
        DemoStep.PickUp => OrderStatus.InTransit,
        DemoStep.Deliver => OrderStatus.Delivered,
        DemoStep.Return => OrderStatus.Returned,
        DemoStep.ReceiveBack => OrderStatus.ReceivedBack,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
    };

    private static ParcelStatus ParcelOf(DemoStep step) => step switch
    {
        DemoStep.PickUp => ParcelStatus.InTransit,
        DemoStep.Deliver => ParcelStatus.Delivered,
        _ => ParcelStatus.Returned,
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo shop {ShopId} seeded: {Steps} steps applied")]
    private static partial void LogSeeded(ILogger logger, Guid shopId, int steps);
}
