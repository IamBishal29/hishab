using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Application.Common.Tenancy;
using Hishab.Domain.Common;
using Hishab.Domain.Orders;
using Hishab.Infrastructure.Demo;
using Hishab.Infrastructure.Persistence;
using Hishab.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hishab.IntegrationTests.Demo;

/// <summary>The app's clock wrapped in a <see cref="SeedClock"/>, as Demo:Seed does in Development.</summary>
public sealed class DemoSeedApiFactory : HishabApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new SeedClock(Clock));
        });
    }
}

public class DemoSeedTests(DemoSeedApiFactory factory) : IClassFixture<DemoSeedApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Seeding_twice_builds_one_complete_demo_shop_and_the_second_run_changes_nothing()
    {
        var seeder = factory.Services.GetRequiredService<DemoSeeder>();

        var first = await seeder.SeedAsync(Ct);
        var second = await seeder.SeedAsync(Ct);

        // The fixture's tests share a database, so "first" may already be a re-run; the state is what counts.
        second.ShopId.ShouldBe(first.ShopId);
        second.StepsApplied.ShouldBe(0);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(first.ShopId);
        var db = scope.ServiceProvider.GetRequiredService<HishabDbContext>();

        var orders = await db.Orders.ToListAsync(Ct);
        orders.Count.ShouldBe(30);
        orders.Select(o => o.Status).Distinct().Order().ShouldBe(Enum.GetValues<OrderStatus>().Order());
        orders.ShouldAllBe(o => o.Status == DemoScript.Orders.Single(d => d.No == o.Number).Outcome);
        orders.Select(o => DhakaTime.DayOf(o.CreatedAt)).Distinct().Count().ShouldBeGreaterThan(20); // a month, not one day
        (await db.Products.CountAsync(Ct)).ShouldBe(DemoScript.Products.Count);
        (await db.Payouts.CountAsync(Ct)).ShouldBe(3);
        (await db.Users.CountAsync(Ct)).ShouldBe(1 + DemoScript.Staff.Count);
    }

    [Fact]
    public async Task Every_phone_number_in_the_demo_shop_is_fictional()
    {
        var shop = await factory.Services.GetRequiredService<DemoSeeder>().SeedAsync(Ct);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(shop.ShopId);
        var db = scope.ServiceProvider.GetRequiredService<HishabDbContext>();

        var customerPhones = await db.Customers.Select(c => c.Phone).ToListAsync(Ct);
        var userPhones = await db.Users.Select(u => u.PhoneNumber).ToListAsync(Ct);
        customerPhones.ShouldNotBeEmpty();
        customerPhones.ShouldAllBe(p => Phone.Parse(p).IsDemo);
        userPhones.ShouldAllBe(p => Phone.Parse(p!).IsDemo);
    }

    [Fact]
    public async Task The_demo_owner_signs_in_like_anyone_else_and_sees_the_shop()
    {
        await factory.Services.GetRequiredService<DemoSeeder>().SeedAsync(Ct);

        var owner = await factory.SignInWithPhoneAsync(_client, "01000000001", Ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/orders?limit=50");
        request.Headers.Authorization = AuthSession.Bearer(owner);
        var page = await (await _client.SendAsync(request, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        owner.GetProperty("role").GetString().ShouldBe("Owner");
        page.GetProperty("items").GetArrayLength().ShouldBe(30);
    }
}
