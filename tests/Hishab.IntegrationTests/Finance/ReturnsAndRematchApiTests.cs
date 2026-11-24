using System.Net;
using System.Text.Json;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.IntegrationTests.Support;

namespace Hishab.IntegrationTests.Finance;

/// <summary>Receive-back with return charges, and payout re-match, over real booked and synced parcels.</summary>
public class ReturnsAndRematchApiTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FinanceScenario _shop = new(factory);

    private Task<JsonElement> Order(JsonElement user, Guid orderId) => _shop.Ok(HttpMethod.Get, $"/api/orders/{orderId}", user);

    private static string Status(JsonElement details) => details.GetProperty("order").GetProperty("status").GetString()!;

    private static JsonElement ReturnCharge(JsonElement details) => details.GetProperty("shipment").GetProperty("returnCharge");

    [Fact]
    public async Task A_packer_receives_the_parcel_and_the_owner_adds_the_charge_later()
    {
        var owner = await _shop.ShopWithFake();
        var packer = await factory.InviteAndSignInAsync(_shop.Client, owner, "Packer", Ct);
        var orderId = await _shop.ReturnedOrder(owner, price: 500m);

        var received = await _shop.Ok(HttpMethod.Post, $"/api/orders/{orderId}/receive-back", packer, new { note = "Box fine" });
        Status(received).ShouldBe("ReceivedBack");
        ReturnCharge(received).ValueKind.ShouldBe(JsonValueKind.Null);

        var charged = await _shop.Ok(HttpMethod.Put, $"/api/orders/{orderId}/return-charge", owner, new { returnCharge = 60m });
        ReturnCharge(charged).GetDecimal().ShouldBe(60m);

        // A cost: hidden from staff who cannot see profit, and they cannot change it.
        ReturnCharge(await Order(packer, orderId)).ValueKind.ShouldBe(JsonValueKind.Null);
        (await _shop.Send(HttpMethod.Put, $"/api/orders/{orderId}/return-charge", packer, new { returnCharge = 0m }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_charge_from_a_packer_is_refused_and_nothing_is_saved()
    {
        var owner = await _shop.ShopWithFake();
        var packer = await factory.InviteAndSignInAsync(_shop.Client, owner, "Packer", Ct);
        var orderId = await _shop.ReturnedOrder(owner, price: 500m);

        var response = await _shop.Send(HttpMethod.Post, $"/api/orders/{orderId}/receive-back", packer, new { returnCharge = 60m });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        Status(await Order(owner, orderId)).ShouldBe("Returned");
    }

    [Fact]
    public async Task The_owner_receives_back_with_the_charge_once()
    {
        var owner = await _shop.ShopWithFake();
        var orderId = await _shop.ReturnedOrder(owner, price: 500m);

        var received = await _shop.Ok(HttpMethod.Post, $"/api/orders/{orderId}/receive-back", owner, new { returnCharge = 45.50m });

        ReturnCharge(received).GetDecimal().ShouldBe(45.50m);
        (await _shop.Send(HttpMethod.Post, $"/api/orders/{orderId}/receive-back", owner)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Only_returned_parcels_are_received_back_or_charged()
    {
        var owner = await _shop.ShopWithFake();
        var (delivered, _) = await _shop.DeliveredOrder(owner, price: 500m);

        (await _shop.Send(HttpMethod.Post, $"/api/orders/{delivered}/receive-back", owner)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await _shop.Send(HttpMethod.Put, $"/api/orders/{delivered}/return-charge", owner, new { returnCharge = 60m }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var bad = await _shop.Send(HttpMethod.Put, $"/api/orders/{delivered}/return-charge", owner, new { returnCharge = -1m });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await bad.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("errors").TryGetProperty("returnCharge", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Rematch_settles_a_parcel_delivered_after_the_payout_was_entered()
    {
        var owner = await _shop.ShopWithFake();
        var (orderId, cn) = await _shop.BookedOrder(owner, price: 440m); // still on the road
        var today = DhakaTime.DayOf(factory.Clock.GetUtcNow());
        var payout = await _shop.Ok(HttpMethod.Post, "/api/money/payouts", owner, new
        {
            courier = "Fake",
            reference = "FK-EARLY",
            paidOn = today,
            netAmount = 440m,
            lines = new[] { new { consignmentId = cn, collected = 500m, deliveryFee = 60m, codFee = 0m } },
        });
        var id = payout.GetProperty("payout").GetProperty("id").GetGuid();
        payout.GetProperty("lines")[0].GetProperty("match").GetString().ShouldBe("NotDelivered");

        await _shop.CourierReports(owner, cn, ParcelStatus.Delivered);
        var rematched = await _shop.Ok(HttpMethod.Post, $"/api/money/payouts/{id}/rematch", owner);

        rematched.GetProperty("lines")[0].GetProperty("match").GetString().ShouldBe("Settled");
        rematched.GetProperty("payout").GetProperty("difference").GetDecimal().ShouldBe(0m);
        Status(await Order(owner, orderId)).ShouldBe("CodSettled");
        (await _shop.Ok(HttpMethod.Get, "/api/money/cod-summary", owner)).GetProperty("collected").GetDecimal().ShouldBe(500m);

        // Running it again changes nothing.
        var again = await _shop.Ok(HttpMethod.Post, $"/api/money/payouts/{id}/rematch", owner);
        again.GetProperty("lines")[0].GetProperty("match").GetString().ShouldBe("Settled");
    }

    [Fact]
    public async Task Another_shops_payouts_and_orders_cannot_be_touched()
    {
        var shopA = await _shop.ShopWithFake();
        var returned = await _shop.ReturnedOrder(shopA, price: 500m);
        var (_, cn) = await _shop.BookedOrder(shopA, price: 440m);
        var today = DhakaTime.DayOf(factory.Clock.GetUtcNow());
        var payout = await _shop.Ok(HttpMethod.Post, "/api/money/payouts", shopA, new
        {
            courier = "Fake",
            reference = "FK-A",
            paidOn = today,
            netAmount = 500m,
            lines = new[] { new { consignmentId = cn, collected = 500m, deliveryFee = 0m, codFee = 0m } },
        });
        var payoutId = payout.GetProperty("payout").GetProperty("id").GetGuid();
        var shopB = await _shop.ShopWithFake();

        (await _shop.Send(HttpMethod.Post, $"/api/money/payouts/{payoutId}/rematch", shopB)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _shop.Send(HttpMethod.Post, $"/api/orders/{returned}/receive-back", shopB)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _shop.Send(HttpMethod.Put, $"/api/orders/{returned}/return-charge", shopB, new { returnCharge = 1m }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Status(await Order(shopA, returned)).ShouldBe("Returned");
    }
}
