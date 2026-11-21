using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Api.Controllers;
using Hishab.Application.Common.Tenancy;
using Hishab.Application.Couriers;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Infrastructure.Persistence;
using Hishab.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.IntegrationTests.Couriers;

/// <summary>
/// A scripted courier standing in for Steadfast: idempotent on the merchant reference (as every
/// adapter must be), and able to lose its next answer after booking, or refuse the next parcel.
/// It has no bulk endpoint, so bulk booking calls it once per parcel; it counts calls in flight.
/// </summary>
public sealed class ScriptedCourier : ICourierProvider
{
    private readonly ConcurrentDictionary<string, string> _parcels = new();

    /// <summary>Book at the courier, then fail as if the response never reached us.</summary>
    public bool LoseNextResponse { get; set; }

    public string? RejectNextWith { get; set; }

    /// <summary>Parcels that exist at the courier, by our reference.</summary>
    public int ParcelsFor(string reference) => _parcels.ContainsKey(reference) ? 1 : 0;

    public int CallsFor(string reference) => _calls.GetValueOrDefault(reference);

    private readonly ConcurrentDictionary<string, int> _calls = new();
    private readonly ConcurrentDictionary<string, bool> _loseFor = new();
    private readonly Lock _gate = new();
    private int _inFlight;
    private int _maxInFlight;

    /// <summary>Book this reference at the courier, but lose the answer (once).</summary>
    public void LoseResponseFor(string reference) => _loseFor[reference] = true;

    /// <summary>Most booking calls that were running at the same time since <see cref="ResetMaxInFlight"/>.</summary>
    public int MaxInFlight
    {
        get
        {
            lock (_gate)
            {
                return _maxInFlight;
            }
        }
    }

    public void ResetMaxInFlight()
    {
        lock (_gate)
        {
            _maxInFlight = 0;
        }
    }

    public CourierProvider Code => CourierProvider.Steadfast;

    public int? MaxBulkBookingSize => null;

    public IReadOnlyList<CourierCredentialField> CredentialFields { get; } = [new("apiKey", "API key")];

    public Task<CredentialCheck> ValidateCredentialsAsync(CourierCredentials credentials, CancellationToken cancellationToken) =>
        Task.FromResult(CredentialCheck.Valid);

    public async Task<BookingResult> BookAsync(CourierCredentials credentials, ParcelRequest parcel, CancellationToken cancellationToken)
    {
        _calls.AddOrUpdate(parcel.MerchantReference, 1, (_, n) => n + 1);
        lock (_gate)
        {
            _maxInFlight = Math.Max(_maxInFlight, ++_inFlight);
        }

        try
        {
            await Task.Delay(50, cancellationToken); // a real network call takes time: lets parallel requests overlap
        }
        finally
        {
            lock (_gate)
            {
                _inFlight--;
            }
        }

        if (RejectNextWith is { } reason)
        {
            RejectNextWith = null;
            return BookingResult.Rejected(parcel.MerchantReference, reason);
        }

        var consignment = _parcels.GetOrAdd(parcel.MerchantReference, _ => "SF" + Random.Shared.Next(10_000_000, 99_999_999));
        if (LoseNextResponse || _loseFor.TryRemove(parcel.MerchantReference, out _))
        {
            LoseNextResponse = false;
            throw new CourierUnavailableException(Code); // booked over there, but we never heard back
        }

        return BookingResult.Booked(parcel.MerchantReference, consignment, "T" + consignment, Money.Bdt(70m));
    }

    public Task<CourierQuote> QuoteAsync(CourierCredentials credentials, ParcelRequest parcel, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<BookingResult>> BookManyAsync(
        CourierCredentials credentials, IReadOnlyList<ParcelRequest> parcels, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ParcelStatusResult> GetStatusAsync(CourierCredentials credentials, string consignmentId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

public sealed class BookingApiFactory : HishabApiFactory
{
    public ScriptedCourier Courier { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Booking:MaxParallelBookings", "2");
        builder.UseSetting("Booking:MaxBatchSize", "10");
        builder.ConfigureTestServices(services => services.AddSingleton<ICourierProvider>(Courier));
    }
}

public partial class BookingTests(BookingApiFactory factory) : IClassFixture<BookingApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, JsonElement user, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = AuthSession.Bearer(user);
        if (method == HttpMethod.Post && path == "/api/orders")
        {
            request.Headers.Add(OrdersController.IdempotencyHeader, Guid.NewGuid().ToString());
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    /// <summary>A shop with Fake and the scripted courier connected (Starter plan: more than one courier).</summary>
    private async Task<JsonElement> ShopWithCouriers()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        await factory.ChangePlanAsync(owner, Domain.Shops.Plan.Starter, Ct);
        (await Send(HttpMethod.Put, "/api/couriers/Fake", owner, new { credentials = new { apiKey = "fk_test_1234567890" } })).EnsureSuccessStatusCode();
        (await Send(HttpMethod.Put, "/api/couriers/Steadfast", owner, new { credentials = new { apiKey = "sf_test_1234567890" } })).EnsureSuccessStatusCode();
        return owner;
    }

    private async Task<Guid> Order(JsonElement user, bool confirm = true, string phone = "01912345678", string? address = "House 4, Road 2, Mirpur")
    {
        var product = await Json(await Send(HttpMethod.Post, "/api/products", user, new { name = "Polo shirt", variants = new[] { new { sku = "POLO-" + Guid.NewGuid().ToString("N")[..6], price = 650m } } }));
        var created = await Send(HttpMethod.Post, "/api/orders", user, new
        {
            phone,
            customerName = "Rahima Begum",
            deliveryAddress = address,
            isInsideDhaka = true,
            source = "Facebook",
            lines = new[] { new { variantId = product.GetProperty("variants")[0].GetProperty("id").GetString(), quantity = 2 } },
        });
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await Json(created)).GetProperty("order").GetProperty("id").GetGuid();
        if (confirm)
        {
            (await Send(HttpMethod.Post, $"/api/orders/{id}/confirm", user)).EnsureSuccessStatusCode();
        }

        return id;
    }

    private Task<HttpResponseMessage> Book(JsonElement user, Guid orderId, string? courier = null) =>
        Send(HttpMethod.Post, $"/api/orders/{orderId}/book", user, new { courier });

    private async Task<List<(ShipmentState State, string? Consignment)>> Shipments(JsonElement user, Guid orderId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(user.GetProperty("shopId").GetGuid());
        var db = scope.ServiceProvider.GetRequiredService<HishabDbContext>();
        return [.. (await db.Shipments.Where(s => s.OrderId == orderId).ToListAsync(Ct)).Select(s => (s.State, s.ConsignmentId))];
    }

    [Fact]
    public async Task Booking_moves_a_confirmed_order_to_booked_and_saves_the_couriers_ids()
    {
        var owner = await ShopWithCouriers();
        var orderId = await Order(owner);

        var response = await Book(owner, orderId, "Fake");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var details = await Json(response);
        details.GetProperty("order").GetProperty("status").GetString().ShouldBe("Booked");
        var shipment = details.GetProperty("shipment");
        shipment.GetProperty("state").GetString().ShouldBe("Booked");
        shipment.GetProperty("provider").GetString().ShouldBe("Fake");
        shipment.GetProperty("consignmentId").GetString()!.ShouldStartWith("FAKE-");
        shipment.GetProperty("trackingCode").GetString().ShouldNotBeNullOrEmpty();
        shipment.GetProperty("deliveryFee").GetDecimal().ShouldBe(70m); // Fake: 60 inside Dhaka + 10 for the second item
        details.GetProperty("order").GetProperty("history").EnumerateArray().Last().GetProperty("note").GetString()
            .ShouldBe($"Booked with Fake: {shipment.GetProperty("consignmentId").GetString()}");
    }

    [Fact]
    public async Task Booking_again_returns_the_same_booking_without_calling_the_courier()
    {
        var owner = await ShopWithCouriers();
        var orderId = await Order(owner);
        var first = await Json(await Book(owner, orderId, "Steadfast"));

        var again = await Book(owner, orderId, "Steadfast");

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(again)).GetProperty("shipment").GetProperty("consignmentId").GetString()
            .ShouldBe(first.GetProperty("shipment").GetProperty("consignmentId").GetString());
        factory.Courier.CallsFor(Shipment.ReferenceFor(orderId)).ShouldBe(1);
    }

    [Fact]
    public async Task Booking_fails_when_order_is_not_confirmed()
    {
        var owner = await ShopWithCouriers();
        var orderId = await Order(owner, confirm: false);

        var response = await Book(owner, orderId, "Fake");

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Json(response)).GetProperty("detail").GetString().ShouldBe("Only confirmed orders can be booked. This order is New.");
        (await Shipments(owner, orderId)).ShouldBeEmpty(); // nothing was sent to a courier
    }

    [Fact]
    public async Task When_the_courier_books_but_the_answer_is_lost_a_retry_gets_the_same_parcel()
    {
        var owner = await ShopWithCouriers();
        var orderId = await Order(owner);
        var reference = Shipment.ReferenceFor(orderId);
        factory.Courier.LoseNextResponse = true;

        var lost = await Book(owner, orderId, "Steadfast");

        lost.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        factory.Courier.ParcelsFor(reference).ShouldBe(1); // the parcel exists at the courier...
        (await Shipments(owner, orderId)).ShouldBe([(ShipmentState.Requested, null)]); // ...and our intent record says so

        var retry = await Book(owner, orderId, "Steadfast");

        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(retry)).GetProperty("order").GetProperty("status").GetString().ShouldBe("Booked");
        factory.Courier.ParcelsFor(reference).ShouldBe(1); // still one parcel: same reference, same parcel
        (await Shipments(owner, orderId)).ShouldHaveSingleItem().State.ShouldBe(ShipmentState.Booked);
    }

    [Fact]
    public async Task An_unfinished_booking_must_be_finished_with_the_same_courier()
    {
        var owner = await ShopWithCouriers();
        var orderId = await Order(owner);
        factory.Courier.LoseNextResponse = true;
        await Book(owner, orderId, "Steadfast");

        var other = await Book(owner, orderId, "Fake");

        other.StatusCode.ShouldBe(HttpStatusCode.Conflict); // Steadfast may already have the parcel: no second rider
        (await Json(other)).GetProperty("detail").GetString().ShouldBe("A booking with Steadfast is not finished for this order. Retry with that courier.");
    }

    [Fact]
    public async Task Two_clicks_at_once_book_one_parcel()
    {
        var owner = await ShopWithCouriers();
        var orderId = await Order(owner);

        var responses = await Task.WhenAll(Book(owner, orderId, "Steadfast"), Book(owner, orderId, "Steadfast"));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
        responses.ShouldContain(r => r.StatusCode == HttpStatusCode.OK);
        factory.Courier.ParcelsFor(Shipment.ReferenceFor(orderId)).ShouldBe(1);
        (await Shipments(owner, orderId)).ShouldHaveSingleItem().State.ShouldBe(ShipmentState.Booked);
    }

    [Fact]
    public async Task A_refused_parcel_is_recorded_and_can_be_booked_again()
    {
        var owner = await ShopWithCouriers();
        var orderId = await Order(owner);
        factory.Courier.RejectNextWith = "Address is outside our coverage.";

        var refused = await Book(owner, orderId, "Steadfast");

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(refused)).GetProperty("errors").GetProperty("courier")[0].GetString()
            .ShouldBe("Steadfast refused this parcel: Address is outside our coverage.");
        (await Shipments(owner, orderId)).ShouldBe([(ShipmentState.Rejected, null)]);

        (await Book(owner, orderId, "Steadfast")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Shipments(owner, orderId)).ShouldHaveSingleItem().State.ShouldBe(ShipmentState.Booked); // same row, retried
    }

    [Fact]
    public async Task The_fake_courier_refuses_phones_ending_in_0000()
    {
        var owner = await ShopWithCouriers();
        var orderId = await Order(owner, phone: "01912340000");

        var refused = await Book(owner, orderId, "Fake");

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(refused)).GetProperty("errors").GetProperty("courier")[0].GetString()!.ShouldContain("recipient unreachable");
    }

    [Fact]
    public async Task Booking_uses_the_shop_default_courier_and_needs_one_that_is_connected()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var orderId = await Order(owner);

        var noCourier = await Book(owner, orderId);
        (await Send(HttpMethod.Put, "/api/shop", owner, new { name = "My shop", defaultCourier = "Fake", deliveryChargeInsideDhaka = 60m, deliveryChargeOutsideDhaka = 120m })).EnsureSuccessStatusCode();
        var notConnected = await Book(owner, orderId);
        (await Send(HttpMethod.Put, "/api/couriers/Fake", owner, new { credentials = new { apiKey = "fk_test_1234567890" } })).EnsureSuccessStatusCode();
        var booked = await Book(owner, orderId);

        (await Json(noCourier)).GetProperty("errors").GetProperty("courier")[0].GetString().ShouldBe("Choose a courier, or set a default courier in shop settings.");
        (await Json(notConnected)).GetProperty("errors").GetProperty("courier")[0].GetString().ShouldBe("Connect Fake in courier settings before booking.");
        booked.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Json(booked)).GetProperty("shipment").GetProperty("provider").GetString().ShouldBe("Fake");
    }

    [Fact]
    public async Task Disconnecting_the_default_courier_clears_the_default()
    {
        var owner = await ShopWithCouriers();
        (await Send(HttpMethod.Put, "/api/shop", owner, new { name = "My shop", defaultCourier = "Fake", deliveryChargeInsideDhaka = 60m, deliveryChargeOutsideDhaka = 120m })).EnsureSuccessStatusCode();

        (await Send(HttpMethod.Delete, "/api/couriers/Fake", owner)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Json(await Send(HttpMethod.Get, "/api/shop", owner))).GetProperty("defaultCourier").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task An_order_without_an_address_cannot_be_booked()
    {
        var owner = await ShopWithCouriers();
        var orderId = await Order(owner, address: null);

        var response = await Book(owner, orderId, "Fake");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(response)).GetProperty("errors").GetProperty("deliveryAddress")[0].GetString().ShouldBe("Add a delivery address before booking.");
    }

    [Fact]
    public async Task Packers_book_but_moderators_cannot_and_other_shops_cannot_see_the_order()
    {
        var owner = await ShopWithCouriers();
        var packer = await factory.InviteAndSignInAsync(_client, owner, "Packer", Ct);
        var moderator = await factory.InviteAndSignInAsync(_client, owner, "Moderator", Ct);
        var otherShop = await factory.SignInAsync(_client, Ct);
        var orderId = await Order(owner);

        (await Book(moderator, orderId, "Fake")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Book(otherShop, orderId, "Fake")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Book(packer, orderId, "Fake")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
