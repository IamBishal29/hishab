using System.Net;
using Hishab.Infrastructure.Persistence;
using Hishab.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.IntegrationTests.Couriers;

/// <summary>
/// #66: courier location lists and an optional destination on bookings and quotes. The Fake
/// courier has a small fixed tree (Dhaka 1 → Mirpur 11 → Mirpur 10 111, ...); the scripted
/// "Steadfast" in this factory has none, like a courier that delivers by address only.
/// </summary>
public partial class BookingTests
{
    private static object Mirpur10 => new { cityId = "1", zoneId = "11", areaId = "111" };

    [Fact]
    public async Task Location_lists_come_level_by_level_sorted_by_name()
    {
        var owner = await ShopWithCouriers();

        var cities = await Json(await Send(HttpMethod.Get, "/api/couriers/Fake/locations", owner));
        var zones = await Json(await Send(HttpMethod.Get, "/api/couriers/Fake/locations?level=Zone&parentId=1", owner));

        cities.EnumerateArray().Select(c => c.GetProperty("name").GetString()).ShouldBe(["Chattogram", "Dhaka"]);
        zones.EnumerateArray().Select(z => (z.GetProperty("id").GetString(), z.GetProperty("name").GetString()))
            .ShouldBe([("12", "Dhanmondi"), ("11", "Mirpur")]);
    }

    [Fact]
    public async Task A_courier_without_locations_or_a_level_without_its_parent_is_a_400()
    {
        var owner = await ShopWithCouriers();

        var steadfast = await Send(HttpMethod.Get, "/api/couriers/Steadfast/locations", owner);
        var noParent = await Send(HttpMethod.Get, "/api/couriers/Fake/locations?level=Area", owner);

        steadfast.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(noParent)).GetProperty("errors").GetProperty("parentId")[0].GetString().ShouldBe("Choose the zone first.");
    }

    [Fact]
    public async Task Booking_with_a_destination_saves_it_on_the_shipment_and_returns_it()
    {
        var owner = await ShopWithCouriers();
        var id = await Order(owner);

        var booked = await Json(await Send(HttpMethod.Post, $"/api/orders/{id}/book", owner, new { courier = "Fake", destination = Mirpur10 }));

        var destination = booked.GetProperty("shipment").GetProperty("destination");
        (destination.GetProperty("cityId").GetString(), destination.GetProperty("zoneId").GetString(), destination.GetProperty("areaId").GetString())
            .ShouldBe(("1", "11", "111"));
        await using var scope = factory.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<HishabDbContext>().Shipments
            .IgnoreQueryFilters().SingleAsync(s => s.OrderId == id, Ct);
        row.DestinationZoneId.ShouldBe("11");
    }

    [Fact]
    public async Task A_zone_from_another_city_is_refused_before_anything_is_saved()
    {
        var owner = await ShopWithCouriers();
        var id = await Order(owner);

        var response = await Send(HttpMethod.Post, $"/api/orders/{id}/book", owner, new
        {
            courier = "Fake",
            destination = new { cityId = "2", zoneId = "11" }, // Mirpur is in Dhaka, not Chattogram
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("errors").GetProperty("destination.zoneId")[0].GetString()
            .ShouldBe("This zone is not in the chosen city.");
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<HishabDbContext>().Shipments
            .IgnoreQueryFilters().AnyAsync(s => s.OrderId == id, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_destination_for_a_courier_that_delivers_by_address_is_a_400()
    {
        var owner = await ShopWithCouriers();
        var id = await Order(owner);

        var response = await Send(HttpMethod.Post, $"/api/orders/{id}/book", owner, new { courier = "Steadfast", destination = Mirpur10 });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("errors").GetProperty("destination")[0].GetString()!.ShouldContain("delivers by address");
    }

    [Fact]
    public async Task A_quote_prices_the_order_and_saves_nothing()
    {
        var owner = await ShopWithCouriers();
        var id = await Order(owner);

        var quote = await Json(await Send(HttpMethod.Post, $"/api/orders/{id}/quote", owner, new { courier = "Fake", destination = Mirpur10 }));

        quote.GetProperty("deliveryFee").GetDecimal().ShouldBeGreaterThan(0m);
        quote.GetProperty("codCharge").GetDecimal().ShouldBeGreaterThanOrEqualTo(0m);
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<HishabDbContext>().Shipments
            .IgnoreQueryFilters().AnyAsync(s => s.OrderId == id, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Moderators_cannot_quote_and_another_shops_order_is_not_found()
    {
        var owner = await ShopWithCouriers();
        var moderator = await factory.InviteAndSignInAsync(_client, owner, "Moderator", Ct);
        var otherShop = await ShopWithCouriers();
        var theirs = await Order(otherShop);

        (await Send(HttpMethod.Post, $"/api/orders/{theirs}/quote", owner, new { courier = "Fake" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Send(HttpMethod.Post, $"/api/orders/{theirs}/quote", moderator, new { courier = "Fake" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
