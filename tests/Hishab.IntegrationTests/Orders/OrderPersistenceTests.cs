using System.Globalization;
using Hishab.Application.Common.Tenancy;
using Hishab.Domain.Common;
using Hishab.Domain.Customers;
using Hishab.Domain.Orders;
using Hishab.Infrastructure.Persistence;
using Hishab.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.IntegrationTests.Orders;

/// <summary>The order aggregate against the real schema (migrations applied): mapping, history and concurrency.</summary>
public class OrderPersistenceTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset _now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    /// <summary>A scope acting for one shop, like one request.</summary>
    private (IServiceScope Scope, HishabDbContext Db) ShopScope(Guid shopId)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(shopId);
        return (scope, scope.ServiceProvider.GetRequiredService<HishabDbContext>());
    }

    private async Task<(Guid ShopId, Guid OrderId)> SavedOrder()
    {
        var owner = await factory.SignInAsync(factory.CreateClient(), Ct);
        var shopId = owner.GetProperty("shopId").GetGuid();
        var (scope, db) = ShopScope(shopId);
        using (scope)
        {
            var phone = "019" + Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);
            var customer = Customer.Create(Phone.Parse(phone), "Rahima", "Mirpur", _now);
            db.Customers.Add(customer);
            var order = Order.Create(
                customer.Id,
                "House 4, Road 2, Mirpur",
                isInsideDhaka: true,
                OrderSource.Facebook,
                [
                    new OrderLineDetails(Guid.NewGuid(), "Polo shirt", "M / Red", "POLO-M-RED", Money.Bdt(650m), Money.Bdt(400m), 2),
                    new OrderLineDetails(null, "Gift wrap", null, null, Money.Bdt(49.50m), null, 1),
                ],
                Money.Bdt(60m),
                Money.Bdt(100m),
                note: "Call before 5 pm",
                createdBy: owner.GetProperty("userId").GetGuid(),
                _now);
            db.Orders.Add(order);
            await db.SaveChangesAsync(Ct);
            return (shopId, order.Id);
        }
    }

    [Fact]
    public async Task Order_with_lines_and_history_round_trips()
    {
        var (shopId, orderId) = await SavedOrder();

        var (scope, db) = ShopScope(shopId);
        using (scope)
        {
            var order = await db.Orders.SingleAsync(o => o.Id == orderId, Ct);

            order.Lines.Count.ShouldBe(2);
            order.Lines.Single(l => l.Sku == "POLO-M-RED").UnitCost.ShouldBe(Money.Bdt(400m));
            order.Subtotal.ShouldBe(Money.Bdt(1_349.50m));
            order.Total.ShouldBe(Money.Bdt(1_409.50m));
            order.CodAmount.ShouldBe(Money.Bdt(1_309.50m));
            order.Source.ShouldBe(OrderSource.Facebook);
            order.History.ShouldHaveSingleItem().To.ShouldBe(OrderStatus.New);
        }
    }

    [Fact]
    public async Task Transitions_are_saved_in_order_with_who_and_when()
    {
        var (shopId, orderId) = await SavedOrder();
        var packer = Guid.NewGuid();

        foreach (var (to, minutes) in new[] { (OrderStatus.Confirmed, 5), (OrderStatus.Booked, 10), (OrderStatus.InTransit, 10) })
        {
            var (scope, db) = ShopScope(shopId);
            using (scope)
            {
                var order = await db.Orders.SingleAsync(o => o.Id == orderId, Ct);
                order.TransitionTo(to, packer, _now.AddMinutes(minutes)); // two steps share a timestamp on purpose
                await db.SaveChangesAsync(Ct);
            }
        }

        var (readScope, readDb) = ShopScope(shopId);
        using (readScope)
        {
            var order = await readDb.Orders.SingleAsync(o => o.Id == orderId, Ct);
            order.Status.ShouldBe(OrderStatus.InTransit);
            order.History.Select(h => h.To).ShouldBe([OrderStatus.New, OrderStatus.Confirmed, OrderStatus.Booked, OrderStatus.InTransit]);
            order.History[^1].From.ShouldBe(OrderStatus.Booked);
            order.History[^1].ChangedBy.ShouldBe(packer);
        }
    }

    [Fact]
    public async Task Second_of_two_concurrent_changes_fails_instead_of_overwriting()
    {
        var (shopId, orderId) = await SavedOrder();
        var (scopeA, packerA) = ShopScope(shopId);
        var (scopeB, packerB) = ShopScope(shopId);
        using (scopeA)
        using (scopeB)
        {
            var seenByA = await packerA.Orders.SingleAsync(o => o.Id == orderId, Ct);
            var seenByB = await packerB.Orders.SingleAsync(o => o.Id == orderId, Ct);

            seenByA.TransitionTo(OrderStatus.Confirmed, Guid.NewGuid(), _now);
            await packerA.SaveChangesAsync(Ct);

            seenByB.TransitionTo(OrderStatus.Cancelled, Guid.NewGuid(), _now, "fake order"); // valid from B's stale view
            await Should.ThrowAsync<DbUpdateConcurrencyException>(() => packerB.SaveChangesAsync(Ct));
        }

        var (readScope, readDb) = ShopScope(shopId);
        using (readScope)
        {
            var order = await readDb.Orders.SingleAsync(o => o.Id == orderId, Ct);
            order.Status.ShouldBe(OrderStatus.Confirmed);
            order.History.Count.ShouldBe(2); // B's step was not written either
        }
    }

    [Fact]
    public async Task Another_shop_cannot_see_the_order()
    {
        var (_, orderId) = await SavedOrder();
        var other = await factory.SignInAsync(factory.CreateClient(), Ct);

        var (scope, db) = ShopScope(other.GetProperty("shopId").GetGuid());
        using (scope)
        {
            (await db.Orders.AnyAsync(o => o.Id == orderId, Ct)).ShouldBeFalse();
        }
    }
}
