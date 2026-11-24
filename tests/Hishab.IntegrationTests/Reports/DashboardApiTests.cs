using System.Globalization;
using System.Net;
using System.Text.Json;
using Hishab.Domain.Common;
using Hishab.IntegrationTests.Finance;
using Hishab.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace Hishab.IntegrationTests.Reports;

/// <summary>GET /api/reports/today and /profit over real orders, parcels, payouts and returns.</summary>
public class DashboardApiTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FinanceScenario _shop = new(factory);

    private DateOnly Today => DhakaTime.DayOf(factory.Clock.GetUtcNow());

    private static decimal Amount(JsonElement figures, string name) => figures.GetProperty(name).GetDecimal();

    [Fact]
    public async Task Today_shows_the_pipeline_and_real_profit_with_what_is_missing()
    {
        var owner = await _shop.ShopWithFake();
        await _shop.DeliveredOrder(owner, price: 1000m, cost: 600m);         // 1060 sale; fee 60 from the quote; COD fee unknown
        var (_, paid) = await _shop.DeliveredOrder(owner, price: 440m);      // 500 sale; no cost price
        await _shop.Ok(HttpMethod.Post, "/api/money/payouts", owner, new
        {
            courier = "Fake",
            reference = "FK-DASH",
            paidOn = Today,
            netAmount = 440m,
            lines = new[] { new { consignmentId = paid, collected = 500m, deliveryFee = 55m, codFee = 5m } },
        });
        var charged = await _shop.ReturnedOrder(owner, price: 500m);
        await _shop.Ok(HttpMethod.Post, $"/api/orders/{charged}/receive-back", owner, new { returnCharge = 60m });
        await _shop.ReturnedOrder(owner, price: 500m);                        // no return charge yet
        await _shop.ConfirmedOrder(owner, price: 300m, advance: 0m);          // waiting to book
        await _shop.BookedOrder(owner, price: 300m);                          // on the road

        var today = await _shop.Ok(HttpMethod.Get, "/api/reports/today", owner);

        var counts = today.GetProperty("counts");
        (counts.GetProperty("new").GetInt32(), counts.GetProperty("toBook").GetInt32(), counts.GetProperty("inTransit").GetInt32(),
            counts.GetProperty("delivered").GetInt32(), counts.GetProperty("returned").GetInt32()).ShouldBe((6, 1, 1, 2, 2));

        var total = today.GetProperty("profit").GetProperty("total");
        Amount(total, "sales").ShouldBe(1560m);
        Amount(total, "productCost").ShouldBe(600m);
        Amount(total, "deliveryFees").ShouldBe(115m);  // 60 quoted + 55 actually deducted by the payout
        Amount(total, "returnCharges").ShouldBe(60m);
        Amount(total, "codFees").ShouldBe(5m);
        Amount(total, "profit").ShouldBe(780m);         // 1560 - 600 - 115 - 60 - 5

        var missing = today.GetProperty("profit").GetProperty("missing");
        (missing.GetProperty("linesWithoutCost").GetInt32(), missing.GetProperty("returnsWithoutCharge").GetInt32(),
            missing.GetProperty("parcelsWithoutDeliveryFee").GetInt32(), missing.GetProperty("parcelsAwaitingPayout").GetInt32())
            .ShouldBe((1, 1, 0, 1));
    }

    [Fact]
    public async Task A_delivery_after_midnight_in_Dhaka_counts_on_the_Dhaka_day()
    {
        var owner = await _shop.ShopWithFake();
        var (late, _) = await _shop.DeliveredOrder(owner, price: 440m);
        var (early, _) = await _shop.DeliveredOrder(owner, price: 440m);
        var midnight = DhakaTime.StartOfDay(Today); // 18:00 UTC the day before
        await MoveDelivery(owner, late, midnight.AddMinutes(-30));  // 23:30 yesterday in Dhaka
        await MoveDelivery(owner, early, midnight.AddMinutes(30));  // 00:30 today in Dhaka (still "yesterday" in UTC)

        var report = await _shop.Ok(HttpMethod.Get, $"/api/reports/profit?from={Today.AddDays(-1):yyyy-MM-dd}&to={Today:yyyy-MM-dd}", owner);

        report.GetProperty("days").EnumerateArray().Select(d => d.GetProperty("figures").GetProperty("delivered").GetInt32()).ShouldBe([1, 1]);
        report.GetProperty("total").GetProperty("sales").GetDecimal().ShouldBe(1000m);
    }

    [Fact]
    public async Task The_month_so_far_is_the_default_and_bad_ranges_are_rejected()
    {
        var owner = await _shop.ShopWithFake();

        var report = await _shop.Ok(HttpMethod.Get, "/api/reports/profit", owner);
        report.GetProperty("from").GetString().ShouldBe(new DateOnly(Today.Year, Today.Month, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        report.GetProperty("days").GetArrayLength().ShouldBe(Today.Day);

        (await _shop.Send(HttpMethod.Get, "/api/reports/profit?from=2026-11-10&to=2026-11-01", owner)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _shop.Send(HttpMethod.Get, "/api/reports/profit?from=2025-01-01&to=2026-11-01", owner)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await _shop.Send(HttpMethod.Get, "/api/reports/profit?from=2026-11-01", owner)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Another_shops_sales_never_appear()
    {
        var shopA = await _shop.ShopWithFake();
        await _shop.DeliveredOrder(shopA, price: 440m, cost: 100m);
        var shopB = await _shop.ShopWithFake();

        var today = await _shop.Ok(HttpMethod.Get, "/api/reports/today", shopB);

        today.GetProperty("counts").GetProperty("new").GetInt32().ShouldBe(0);
        today.GetProperty("profit").GetProperty("total").GetProperty("sales").GetDecimal().ShouldBe(0m);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Packer")]
    public async Task Only_the_owner_sees_reports(string role)
    {
        var owner = await _shop.ShopWithFake();
        var staff = await factory.InviteAndSignInAsync(_shop.Client, owner, role, Ct);

        (await _shop.Send(HttpMethod.Get, "/api/reports/today", staff)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _shop.Send(HttpMethod.Get, "/api/reports/profit", staff)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task New_history_rows_carry_their_shops_tenant()
    {
        var owner = await _shop.ShopWithFake();
        var orderId = await _shop.ConfirmedOrder(owner, price: 300m, advance: 0m);

        await _shop.InShop(owner, async db =>
        {
            var tenants = await db.Database.SqlQuery<Guid>($"SELECT tenant_id AS \"Value\" FROM order_status_changes WHERE order_id = {orderId}").ToListAsync(Ct);
            tenants.ShouldBe([FinanceScenario.ShopId(owner), FinanceScenario.ShopId(owner)]); // New, Confirmed
            return 0;
        });
    }

    private Task MoveDelivery(JsonElement owner, Guid orderId, DateTimeOffset at) =>
        _shop.InShop(owner, db => db.Database.ExecuteSqlAsync(
            $"UPDATE order_status_changes SET changed_at = {at} WHERE order_id = {orderId} AND \"to\" = 'Delivered'", Ct));
}
