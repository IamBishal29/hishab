using System.Net;
using System.Text.Json;
using Hishab.Application.Common.Tenancy;
using Hishab.Application.Shops;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Persistence;
using Hishab.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.IntegrationTests.Shops;

public class PlanLimitTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    private string CurrentPeriod => BillingPeriod.Containing(factory.Clock.GetUtcNow()).Key;

    /// <summary>Runs <paramref name="action"/> in its own request-like scope for the shop (orders will call the service the same way).</summary>
    private async Task<T> AsShop<T>(Guid shopId, Func<IServiceProvider, Task<T>> action)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(shopId);
        return await action(scope.ServiceProvider);
    }

    private Task<bool> TryReserveOrder(Guid shopId) => AsShop(shopId, async sp =>
    {
        try
        {
            await sp.GetRequiredService<IPlanLimitService>().ReserveOrderAsync(Ct);
            return true;
        }
        catch (PlanLimitExceededException)
        {
            return false;
        }
    });

    private Task<int> SeedOrders(Guid shopId, int count) => AsShop(shopId, async sp =>
    {
        var db = sp.GetRequiredService<HishabDbContext>();
        db.UsageCounters.Add(new UsageCounter(CurrentPeriod, UsageMetric.Orders, count));
        return await db.SaveChangesAsync(Ct);
    });

    private Task<int> OrdersUsed(Guid shopId, string period) => AsShop(shopId, sp =>
        sp.GetRequiredService<HishabDbContext>().UsageCounters
            .Where(c => c.Period == period && c.Metric == UsageMetric.Orders)
            .Select(c => c.Count)
            .SingleOrDefaultAsync(Ct));

    private async Task<Guid> NewShop(Plan plan = Plan.Free)
    {
        var owner = await factory.SignInAsync(_client, Ct);
        if (plan != Plan.Free)
        {
            await factory.ChangePlanAsync(owner, plan, Ct);
        }

        return owner.GetProperty("shopId").GetGuid();
    }

    [Fact]
    public async Task Usage_reports_plan_period_and_used_versus_limit()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        await TryReserveOrder(owner.GetProperty("shopId").GetGuid());

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/shop/usage");
        request.Headers.Authorization = AuthSession.Bearer(owner);
        var response = await _client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var usage = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        usage.GetProperty("plan").GetString().ShouldBe("Free");
        usage.GetProperty("period").GetString().ShouldBe(CurrentPeriod);
        usage.GetProperty("orders").GetProperty("used").GetInt32().ShouldBe(1);
        usage.GetProperty("orders").GetProperty("limit").GetInt32().ShouldBe(100);
        usage.GetProperty("users").GetProperty("used").GetInt32().ShouldBe(1);
        usage.GetProperty("users").GetProperty("limit").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Orders_stop_at_the_monthly_limit()
    {
        var shopId = await NewShop(); // Free: 100 a month
        await SeedOrders(shopId, 99);

        (await TryReserveOrder(shopId)).ShouldBeTrue(); // the 100th
        (await TryReserveOrder(shopId)).ShouldBeFalse();
        (await OrdersUsed(shopId, CurrentPeriod)).ShouldBe(100);
    }

    [Fact]
    public async Task Parallel_orders_at_the_limit_never_overshoot()
    {
        var shopId = await NewShop();
        await SeedOrders(shopId, 95); // 5 left

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => TryReserveOrder(shopId)));

        results.Count(ok => ok).ShouldBe(5);
        (await OrdersUsed(shopId, CurrentPeriod)).ShouldBe(100);
    }

    [Fact]
    public async Task Quota_resets_when_the_Dhaka_month_changes()
    {
        var shopId = await NewShop();
        await SeedOrders(shopId, 100);
        (await TryReserveOrder(shopId)).ShouldBeFalse();

        var nextMonth = BillingPeriod.Containing(factory.Clock.GetUtcNow()).EndsAt;
        factory.Clock.SetUtcNow(nextMonth); // 00:00 on the 1st in Dhaka (18:00 UTC the day before)

        (await TryReserveOrder(shopId)).ShouldBeTrue();
        (await OrdersUsed(shopId, BillingPeriod.Containing(nextMonth).Key)).ShouldBe(1);
    }

    [Fact]
    public async Task Business_plan_has_no_order_limit()
    {
        var shopId = await NewShop(Plan.Business);
        await SeedOrders(shopId, 1_000_000);

        (await TryReserveOrder(shopId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Each_shop_has_its_own_quota()
    {
        var full = await NewShop();
        var other = await NewShop();
        await SeedOrders(full, 100);

        (await TryReserveOrder(full)).ShouldBeFalse();
        (await TryReserveOrder(other)).ShouldBeTrue();
    }
}
