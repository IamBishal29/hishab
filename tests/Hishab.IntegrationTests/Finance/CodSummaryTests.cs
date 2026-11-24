using System.Net;
using Hishab.Domain.Common;
using Hishab.IntegrationTests.Support;

namespace Hishab.IntegrationTests.Finance;

/// <summary>GET /api/money/cod-summary over real orders, delivered through the status sync job and paid by payouts.</summary>
public class CodSummaryTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FinanceScenario _shop = new(factory);

    private Task<System.Text.Json.JsonElement> Pay(System.Text.Json.JsonElement owner, string consignment, decimal collected) =>
        _shop.Ok(HttpMethod.Post, "/api/money/payouts", owner, new
        {
            courier = "Fake",
            reference = "PAY-" + Guid.NewGuid().ToString("N")[..8],
            paidOn = DhakaTime.DayOf(factory.Clock.GetUtcNow()),
            netAmount = collected,
            lines = new[] { new { consignmentId = consignment, collected, deliveryFee = 0m, codFee = 0m } },
        });

    [Fact]
    public async Task Summary_reconciles_delivered_cod_against_payouts_with_aged_outstanding()
    {
        var owner = await _shop.ShopWithFake();
        await _shop.DeliveredOrder(owner, price: 650m, advance: 0m, daysAgo: 1);          // 710.00 due, young
        await _shop.DeliveredOrder(owner, price: 1200.50m, advance: 200m, daysAgo: 10);   // 1060.50 due, overdue
        var (_, paid) = await _shop.DeliveredOrder(owner, price: 400m, advance: 0m, daysAgo: 3); // 460.00 due
        await Pay(owner, paid, 460m);
        var (_, shortPaid) = await _shop.DeliveredOrder(owner, price: 940m, advance: 0m, daysAgo: 3); // 1000.00 due
        await Pay(owner, shortPaid, 950m);
        await _shop.ConfirmedOrder(owner, price: 999m, advance: 0m); // not delivered: nothing expected yet

        var summary = await _shop.Ok(HttpMethod.Get, "/api/money/cod-summary", owner);

        var fake = summary.GetProperty("couriers").EnumerateArray().ShouldHaveSingleItem();
        fake.GetProperty("courier").GetString().ShouldBe("Fake");
        fake.GetProperty("deliveredParcels").GetInt32().ShouldBe(4);
        fake.GetProperty("expected").GetDecimal().ShouldBe(3230.50m);
        fake.GetProperty("collected").GetDecimal().ShouldBe(1410m);
        fake.GetProperty("shortfall").GetDecimal().ShouldBe(50m);
        fake.GetProperty("shortPaidParcels").GetInt32().ShouldBe(1);
        fake.GetProperty("outstanding").GetDecimal().ShouldBe(1770.50m);
        fake.GetProperty("outstandingParcels").GetInt32().ShouldBe(2);
        fake.GetProperty("oldestOutstandingDays").GetInt32().ShouldBe(10);
        fake.GetProperty("outstandingByAge").EnumerateArray()
            .Select(b => (b.GetProperty("age").GetString(), b.GetProperty("parcels").GetInt32(), b.GetProperty("amount").GetDecimal()))
            .ShouldBe([("Days0To3", 1, 710m), ("Days4To7", 0, 0m), ("Days8To14", 1, 1060.50m), ("Days15Plus", 0, 0m)]);
        summary.GetProperty("expected").GetDecimal().ShouldBe(3230.50m);
        summary.GetProperty("shortfall").GetDecimal().ShouldBe(50m);
        summary.GetProperty("outstanding").GetDecimal().ShouldBe(1770.50m);
    }

    [Fact]
    public async Task Another_shop_sees_none_of_this_shops_cash()
    {
        var shopA = await _shop.ShopWithFake();
        await _shop.DeliveredOrder(shopA, price: 650m, advance: 0m, daysAgo: 2);
        var shopB = await factory.SignInAsync(_shop.Client, Ct);

        var summary = await _shop.Ok(HttpMethod.Get, "/api/money/cod-summary", shopB);

        summary.GetProperty("couriers").GetArrayLength().ShouldBe(0);
        summary.GetProperty("expected").GetDecimal().ShouldBe(0m);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Moderator")]
    [InlineData("Packer")]
    public async Task Only_the_owner_sees_the_shops_money(string role)
    {
        var owner = await factory.SignInAsync(_shop.Client, Ct);
        var staff = await factory.InviteAndSignInAsync(_shop.Client, owner, role, Ct);

        (await _shop.Send(HttpMethod.Get, "/api/money/cod-summary", staff)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _shop.Send(HttpMethod.Get, "/api/money/cod-summary", owner)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
