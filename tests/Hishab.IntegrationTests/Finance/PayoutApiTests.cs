using System.Net;
using System.Text.Json;
using Hishab.Domain.Common;
using Hishab.IntegrationTests.Support;

namespace Hishab.IntegrationTests.Finance;

/// <summary>POST/GET /api/money/payouts over real delivered orders.</summary>
public class PayoutApiTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FinanceScenario _shop = new(factory);

    private DateOnly Today => DhakaTime.DayOf(factory.Clock.GetUtcNow());

    private object Payout(string reference, decimal net, params object[] lines) =>
        new { courier = "Fake", reference, paidOn = Today, netAmount = net, lines };

    private static object Line(string consignment, decimal collected, decimal deliveryFee = 0m, decimal codFee = 0m) =>
        new { consignmentId = consignment, collected, deliveryFee, codFee };

    private async Task<string> Status(JsonElement owner, Guid orderId) =>
        (await _shop.Ok(HttpMethod.Get, $"/api/orders/{orderId}", owner)).GetProperty("order").GetProperty("status").GetString()!;

    private static IEnumerable<(string?, string?)> Matches(JsonElement payout) =>
        payout.GetProperty("lines").EnumerateArray().Select(l => (l.GetProperty("consignmentId").GetString(), l.GetProperty("match").GetString()));

    [Fact]
    public async Task Payout_settles_matched_orders_and_lists_every_mismatch_with_its_reason()
    {
        var owner = await _shop.ShopWithFake();
        var (exact, exactCn) = await _shop.DeliveredOrder(owner, price: 1140m);  // 1200 due
        var (shortPaid, shortCn) = await _shop.DeliveredOrder(owner, price: 940m); // 1000 due
        var (onTheRoad, roadCn) = await _shop.BookedOrder(owner, price: 440m);    // not delivered yet

        var response = await _shop.Send(HttpMethod.Post, "/api/money/payouts", owner, Payout(
            "FK-PAY-1",
            net: 2000m,
            Line(exactCn, 1200m, deliveryFee: 60m, codFee: 12m),
            Line(shortCn, 950m, deliveryFee: 60m, codFee: 10m),
            Line(roadCn, 500m),
            Line("NOT-OURS-1", 300m),
            Line(exactCn, 1200m)));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var payout = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        Matches(payout).ShouldBe([
            (exactCn, "Settled"),
            (shortCn, "AmountDiffers"),
            (roadCn, "NotDelivered"),
            ("NOT-OURS-1", "UnknownParcel"),
            (exactCn, "RepeatedInPayout"),
        ]);
        var shortLine = payout.GetProperty("lines")[1];
        shortLine.GetProperty("codDue").GetDecimal().ShouldBe(1000m);
        shortLine.GetProperty("shortfall").GetDecimal().ShouldBe(50m);
        shortLine.GetProperty("orderNumber").GetInt32().ShouldBePositive();

        // The books: 4,150 collected - 120 delivery - 22 COD fees = 4,008 on paper; 2,000 arrived.
        var summary = payout.GetProperty("payout");
        summary.GetProperty("statementNet").GetDecimal().ShouldBe(4008m);
        summary.GetProperty("difference").GetDecimal().ShouldBe(-2008m);
        summary.GetProperty("settledParcels").GetInt32().ShouldBe(2);
        summary.GetProperty("mismatchedParcels").GetInt32().ShouldBe(4);

        (await Status(owner, exact)).ShouldBe("CodSettled");
        (await Status(owner, shortPaid)).ShouldBe("CodSettled");
        (await Status(owner, onTheRoad)).ShouldBe("Booked");
    }

    [Fact]
    public async Task The_same_statement_entered_twice_is_refused_and_settles_nothing_twice()
    {
        var owner = await _shop.ShopWithFake();
        var (_, cn) = await _shop.DeliveredOrder(owner, price: 440m);
        var body = Payout("FK-PAY-DUP", 500m, Line(cn, 500m));

        (await _shop.Send(HttpMethod.Post, "/api/money/payouts", owner, body)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var again = await _shop.Send(HttpMethod.Post, "/api/money/payouts", owner, body);

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var list = await _shop.Ok(HttpMethod.Get, "/api/money/payouts", owner);
        list.GetProperty("total").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task A_parcel_on_a_second_statement_is_already_paid()
    {
        var owner = await _shop.ShopWithFake();
        var (_, cn) = await _shop.DeliveredOrder(owner, price: 440m);
        await _shop.Ok(HttpMethod.Post, "/api/money/payouts", owner, Payout("FK-PAY-A", 500m, Line(cn, 500m)));

        var second = await _shop.Ok(HttpMethod.Post, "/api/money/payouts", owner, Payout("FK-PAY-B", 500m, Line(cn, 500m)));

        Matches(second).ShouldBe([(cn, "AlreadyPaid")]);
        (await _shop.Ok(HttpMethod.Get, "/api/money/cod-summary", owner)).GetProperty("collected").GetDecimal().ShouldBe(500m);
    }

    [Fact]
    public async Task Two_payouts_settling_the_same_parcel_at_once_settle_it_once()
    {
        var owner = await _shop.ShopWithFake();
        var (orderId, cn) = await _shop.DeliveredOrder(owner, price: 440m);

        var responses = await Task.WhenAll(
            _shop.Send(HttpMethod.Post, "/api/money/payouts", owner, Payout("FK-RACE-1", 500m, Line(cn, 500m))),
            _shop.Send(HttpMethod.Post, "/api/money/payouts", owner, Payout("FK-RACE-2", 500m, Line(cn, 500m))));

        // Either the loser saw the order settled (201, AlreadyPaid) or it lost the race at save time (409).
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBeGreaterThanOrEqualTo(1);
        (await Status(owner, orderId)).ShouldBe("CodSettled");
        (await _shop.Ok(HttpMethod.Get, "/api/money/cod-summary", owner)).GetProperty("collected").GetDecimal().ShouldBe(500m);
    }

    [Fact]
    public async Task Another_shops_parcel_is_unknown_and_its_payouts_are_invisible()
    {
        var shopA = await _shop.ShopWithFake();
        var (orderA, cnA) = await _shop.DeliveredOrder(shopA, price: 440m);
        var shopB = await _shop.ShopWithFake();

        var payoutB = await _shop.Ok(HttpMethod.Post, "/api/money/payouts", shopB, Payout("FK-PAY-X", 500m, Line(cnA, 500m)));

        Matches(payoutB).ShouldBe([(cnA, "UnknownParcel")]);
        (await Status(shopA, orderA)).ShouldBe("Delivered");
        var id = payoutB.GetProperty("payout").GetProperty("id").GetGuid();
        (await _shop.Send(HttpMethod.Get, $"/api/money/payouts/{id}", shopA)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _shop.Ok(HttpMethod.Get, $"/api/money/payouts/{id}", shopB)).GetProperty("lines").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_lines_are_rejected_per_field()
    {
        var owner = await _shop.ShopWithFake();

        var response = await _shop.Send(HttpMethod.Post, "/api/money/payouts", owner, Payout("FK-BAD", 10m, Line("", 10m), Line("CN-2", 10.005m)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("errors");
        errors.TryGetProperty("lines[0].consignmentId", out _).ShouldBeTrue();
        errors.TryGetProperty("lines[1].collected", out _).ShouldBeTrue();
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Packer")]
    public async Task Only_the_owner_records_payouts(string role)
    {
        var owner = await _shop.ShopWithFake();
        var staff = await factory.InviteAndSignInAsync(_shop.Client, owner, role, Ct);

        (await _shop.Send(HttpMethod.Post, "/api/money/payouts", staff, Payout("FK-ROLE", 1m, Line("CN", 1m)))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await _shop.Send(HttpMethod.Get, "/api/money/payouts", staff)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
