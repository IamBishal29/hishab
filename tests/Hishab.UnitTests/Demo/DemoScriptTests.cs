using Hishab.Domain.Orders;
using Hishab.Infrastructure.Demo;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.UnitTests.Demo;

public class DemoScriptTests
{
    // Just after midnight and late evening in Dhaka (UTC+6): the edges where "today" steps could fall in the future.
    public static TheoryData<DateTimeOffset> Nows => new()
    {
        new DateTimeOffset(2026, 11, 26, 0, 30, 0, TimeSpan.FromHours(6)),
        new DateTimeOffset(2026, 11, 26, 23, 30, 0, TimeSpan.FromHours(6)),
    };

    [Theory]
    [MemberData(nameof(Nows))]
    public void Every_step_is_in_the_past_and_in_time_order(DateTimeOffset now)
    {
        var timeline = DemoScript.Timeline(now);

        timeline.ShouldAllBe(e => e.At < now);
        timeline.Select(e => e.At).ShouldBeInOrder();
    }

    [Theory]
    [MemberData(nameof(Nows))]
    public void Each_order_steps_forward_in_time_and_settled_orders_are_paid_once(DateTimeOffset now)
    {
        var timeline = DemoScript.Timeline(now);

        foreach (var order in DemoScript.Orders)
        {
            var steps = timeline.Where(e => e.Order == order).ToList();
            steps[0].Step.ShouldBe(DemoStep.Create);
            steps.Select(e => e.At).ShouldBeInOrder();
        }

        var paid = timeline.Where(e => e.Step == DemoStep.Payout).SelectMany(e => e.Payout!.Orders).ToList();
        paid.ShouldBe(DemoScript.Orders.Where(o => o.Outcome == OrderStatus.CodSettled), ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(Nows))]
    public void Orders_are_created_in_script_order_so_order_numbers_match_it(DateTimeOffset now)
    {
        DemoScript.Timeline(now).Where(e => e.Step == DemoStep.Create).Select(e => e.Order!.No)
            .ShouldBe(DemoScript.Orders.Select(o => o.No));
    }

    [Fact]
    public void The_shop_shows_every_order_status()
    {
        DemoScript.Orders.Select(o => o.Outcome).Distinct().Order()
            .ShouldBe(Enum.GetValues<OrderStatus>().Order());
    }

    [Fact]
    public void Every_person_has_a_distinct_fictional_number()
    {
        var phones = DemoScript.Customers.Select(c => c.Phone)
            .Concat(DemoScript.Staff.Select(s => s.Phone))
            .Append(DemoScript.OwnerPhone)
            .ToList();

        phones.ShouldAllBe(p => p.IsDemo);
        phones.Distinct().Count().ShouldBe(phones.Count);
    }

    [Fact]
    public void Orders_only_use_products_and_customers_that_exist()
    {
        var skus = DemoScript.Products.SelectMany(p => p.Variants).Select(v => v.Sku).ToHashSet();

        DemoScript.Orders.SelectMany(o => o.Lines).ShouldAllBe(l => skus.Contains(l.Sku));
        DemoScript.Orders.ShouldAllBe(o => DemoScript.Customers.Any(c => c.Index == o.Customer));
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Demo", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("Testing", false)]
    [InlineData("Developmnet", false)] // a typo never seeds
    public void Demo_data_is_allowed_only_in_development_and_demo(string environment, bool allowed)
    {
        DemoSeedSetup.IsAllowedIn(environment).ShouldBe(allowed);
    }

    [Fact]
    public async Task Startup_refuses_to_seed_when_the_flag_is_on_outside_development_and_demo()
    {
        var services = new ServiceCollection();
        services.Configure<DemoSeedOptions>(o => o.Seed = true);
        await using var provider = services.BuildServiceProvider();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => provider.SeedDemoDataIfEnabledAsync("Production"));

        ex.Message.ShouldContain("'Production'");
    }
}
