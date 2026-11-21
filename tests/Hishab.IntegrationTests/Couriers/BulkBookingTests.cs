using System.Net;
using System.Text.Json;
using Hishab.Domain.Couriers;
using Hishab.IntegrationTests.Support;

namespace Hishab.IntegrationTests.Couriers;

/// <summary>POST /api/orders/bulk/book: partial success, bounded concurrency, safe retries.</summary>
public partial class BookingTests
{
    private Task<HttpResponseMessage> BulkBook(JsonElement user, IEnumerable<Guid> orderIds, string? courier = null) =>
        Send(HttpMethod.Post, "/api/orders/bulk/book", user, new { orderIds, courier });

    private static Dictionary<Guid, (bool Succeeded, string? Error)> ResultsById(JsonElement body) =>
        body.GetProperty("results").EnumerateArray().ToDictionary(
            r => r.GetProperty("orderId").GetGuid(),
            r => (r.GetProperty("succeeded").GetBoolean(), r.GetProperty("error").GetString()));

    [Fact]
    public async Task Bulk_booking_books_what_it_can_and_reports_each_failure()
    {
        var owner = await ShopWithCouriers();
        var good1 = await Order(owner);
        var good2 = await Order(owner);
        var refused = await Order(owner, phone: "01912340000"); // the Fake courier refuses it
        var notConfirmed = await Order(owner, confirm: false);
        var noAddress = await Order(owner, address: null);
        var unknown = Guid.NewGuid();

        var response = await BulkBook(owner, [good1, refused, notConfirmed, good1, noAddress, unknown, good2], "Fake");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await Json(response);
        body.GetProperty("succeeded").GetInt32().ShouldBe(2);
        body.GetProperty("failed").GetInt32().ShouldBe(4);
        body.GetProperty("results").EnumerateArray().Select(r => r.GetProperty("orderId").GetGuid())
            .ShouldBe([good1, refused, notConfirmed, noAddress, unknown, good2]); // request order, duplicates once
        var results = ResultsById(body);
        results[good1].ShouldBe((true, null));
        results[good2].ShouldBe((true, null));
        results[refused].ShouldBe((false, "courierRejected"));
        results[notConfirmed].ShouldBe((false, "invalidTransition"));
        results[noAddress].ShouldBe((false, "invalid"));
        results[unknown].ShouldBe((false, "notFound"));

        (await Shipments(owner, good1)).ShouldHaveSingleItem().Consignment!.ShouldStartWith("FAKE-");
        (await Shipments(owner, refused)).ShouldBe([(ShipmentState.Rejected, null)]);
        (await Shipments(owner, notConfirmed)).ShouldBeEmpty(); // never sent to the courier
    }

    [Fact]
    public async Task Without_a_bulk_endpoint_a_few_calls_run_at_once_and_a_lost_answer_is_safe_to_retry()
    {
        var owner = await ShopWithCouriers();
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            ids.Add(await Order(owner));
        }

        var lost = ids[3];
        factory.Courier.LoseResponseFor(Shipment.ReferenceFor(lost));
        factory.Courier.ResetMaxInFlight();

        var first = await Json(await BulkBook(owner, ids, "Steadfast"));

        factory.Courier.MaxInFlight.ShouldBe(2); // Booking:MaxParallelBookings in the test factory
        first.GetProperty("succeeded").GetInt32().ShouldBe(5);
        ResultsById(first)[lost].ShouldBe((false, "courierUnavailable"));
        (await Shipments(owner, lost)).ShouldBe([(ShipmentState.Requested, null)]); // the parcel may exist: intent kept

        var retry = await Json(await BulkBook(owner, ids, "Steadfast"));

        retry.GetProperty("succeeded").GetInt32().ShouldBe(6);
        factory.Courier.ParcelsFor(Shipment.ReferenceFor(lost)).ShouldBe(1); // same reference, same parcel
        factory.Courier.CallsFor(Shipment.ReferenceFor(ids[0])).ShouldBe(1); // booked orders are not sent again
        (await Shipments(owner, lost)).ShouldHaveSingleItem().State.ShouldBe(ShipmentState.Booked);
    }

    [Fact]
    public async Task Bulk_booking_needs_a_courier_a_packer_and_a_batch_within_the_limit()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var moderator = await factory.InviteAndSignInAsync(_client, owner, "Moderator", Ct);
        var orderId = await Order(owner);

        var noCourier = await BulkBook(owner, [orderId]);
        var tooMany = await BulkBook(owner, Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()), "Fake");
        var forbidden = await BulkBook(moderator, [orderId], "Fake");

        (await Json(noCourier)).GetProperty("errors").GetProperty("courier")[0].GetString().ShouldBe("Choose a courier, or set a default courier in shop settings.");
        (await Json(tooMany)).GetProperty("errors").GetProperty("orderIds")[0].GetString().ShouldBe("At most 10 orders at a time.");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Shipments(owner, orderId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Bulk_booking_cannot_touch_another_shops_orders()
    {
        var owner = await ShopWithCouriers();
        var otherShop = await ShopWithCouriers();
        var theirs = await Order(otherShop);

        var body = await Json(await BulkBook(owner, [theirs], "Fake"));

        ResultsById(body)[theirs].ShouldBe((false, "notFound"));
        (await Shipments(otherShop, theirs)).ShouldBeEmpty();
    }
}
