using System.Collections.Concurrent;
using System.Text.Json;
using Hishab.Application.Couriers;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Infrastructure.Couriers.Pathao;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Hishab.IntegrationTests.Couriers.Pathao;

/// <summary>
/// Contract tests: the real adapter and HTTP pipeline against a WireMock stand-in that answers the
/// way Pathao's merchant API does. No test ever calls Pathao.
/// </summary>
public sealed class PathaoContractTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string TokenPath = "/aladdin/api/v1/issue-token";
    private const string OrdersPath = "/aladdin/api/v1/orders";

    private static readonly string[] _shortAddress = ["The recipient address must be at least 10 characters."];
    private static readonly string[] _duplicateReference = ["The merchant order id has already been taken."];

    private readonly WireMockServer _pathao = WireMockServer.Start();
    // The real clock: the resilience pipeline waits on the DI TimeProvider, and a fake one that
    // never advances would stall every retry backoff forever.
    private readonly TimeProvider _clock = TimeProvider.System;
    private readonly ServiceProvider _services;
    private readonly ConcurrentBag<IReadOnlyDictionary<string, string>> _saved = [];

    public PathaoContractTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Couriers:Pathao:BaseUrl"] = _pathao.Url + "/" })
            .Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(_clock);
        PathaoSetup.AddPathao(services, configuration);
        services.PostConfigure<HttpStandardResilienceOptions>(PathaoCourierProvider.HttpClientName + "-standard", o =>
        {
            o.Retry.Delay = TimeSpan.FromMilliseconds(1); // same retries, no waiting in tests
            o.Retry.UseJitter = false;
        });
        _services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _pathao.Stop();
    }

    private ICourierProvider Pathao => _services.GetRequiredService<ICourierProvider>();

    private CourierCredentials Connected(string accessToken = "acc-1", TimeSpan? validFor = null) => new(
        new Dictionary<string, string>
        {
            ["clientId"] = "cid",
            ["clientSecret"] = "csecret",
            ["storeId"] = "123",
            ["accessToken"] = accessToken,
            ["accessTokenExpiresAt"] = _clock.GetUtcNow().Add(validFor ?? TimeSpan.FromDays(4)).ToString("O"),
            ["refreshToken"] = "ref-1",
        },
        (values, _) =>
        {
            _saved.Add(values);
            return Task.CompletedTask;
        });

    private static ParcelRequest Parcel(string reference = "8d3f0c7a1b2e4f60a9c8b7d6e5f4a3b2") =>
        new(reference, "Rahima Begum", "+8801712345678", "House 4, Road 2, Mirpur 10, Dhaka", true, Money.Bdt(1360m), 2, "Call before 5 pm");

    private void TokenGrant(string grantType, int status, object? body = null) =>
        _pathao.Given(Request.Create().WithPath(TokenPath).UsingPost().WithBody(new JsonPartialMatcher(new { grant_type = grantType })))
            .RespondWith(Response.Create().WithStatusCode(status).WithBodyAsJson(body ?? new { message = "Unauthorized" }));

    private static object Tokens(string access, string refresh) =>
        new { token_type = "Bearer", expires_in = 432_000, access_token = access, refresh_token = refresh };

    private void OrderCreated(string token = "acc-1") =>
        _pathao.Given(Request.Create().WithPath(OrdersPath).UsingPost().WithHeader("Authorization", $"Bearer {token}"))
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                message = "Order Created Successfully",
                type = "success",
                code = 200,
                data = new { consignment_id = "DL121224VS8TTJ", merchant_order_id = "x", order_status = "Pending", delivery_fee = 60 },
            }));

    private int Calls(string path, string method = "POST") =>
        _pathao.LogEntries.Count(e => e.RequestMessage!.Path == path && e.RequestMessage.Method == method);

    [Fact]
    public async Task Connecting_trades_the_login_for_tokens_and_keeps_no_password()
    {
        TokenGrant("password", 200, Tokens("acc-1", "ref-1"));
        var typed = new CourierCredentials(new Dictionary<string, string>
        {
            ["clientId"] = "cid",
            ["clientSecret"] = "csecret",
            ["storeId"] = "123",
            ["username"] = "shop@example.com",
            ["password"] = "s3cret!",
        });

        var check = await Pathao.ValidateCredentialsAsync(typed, Ct);

        check.IsValid.ShouldBeTrue();
        check.Keep!.Keys.Order().ShouldBe(["accessToken", "accessTokenExpiresAt", "clientId", "clientSecret", "refreshToken", "storeId"]);
        check.Keep.Values.ShouldNotContain("s3cret!");
        DateTimeOffset.Parse(check.Keep["accessTokenExpiresAt"], System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe(_clock.GetUtcNow().AddSeconds(432_000), tolerance: TimeSpan.FromMinutes(1)); // expires_in from Pathao
    }

    [Fact]
    public async Task A_wrong_login_is_refused_without_storing_anything()
    {
        TokenGrant("password", 401);
        var typed = new CourierCredentials(new Dictionary<string, string>
        {
            ["clientId"] = "cid",
            ["clientSecret"] = "csecret",
            ["storeId"] = "123",
            ["username"] = "shop@example.com",
            ["password"] = "wrong",
        });

        var check = await Pathao.ValidateCredentialsAsync(typed, Ct);

        check.IsValid.ShouldBeFalse();
        check.Keep.ShouldBeNull();
    }

    [Fact]
    public async Task Booking_sends_pathaos_fields_and_reads_the_consignment()
    {
        OrderCreated();

        var result = await Pathao.BookAsync(Connected(), Parcel(), Ct);

        result.ShouldBe(BookingResult.Booked("8d3f0c7a1b2e4f60a9c8b7d6e5f4a3b2", "DL121224VS8TTJ", "DL121224VS8TTJ", Money.Bdt(60m)));
        var sent = JsonDocument.Parse(_pathao.LogEntries.Single().RequestMessage!.Body!).RootElement;
        sent.GetProperty("store_id").GetInt32().ShouldBe(123);
        sent.GetProperty("merchant_order_id").GetString().ShouldBe("8d3f0c7a1b2e4f60a9c8b7d6e5f4a3b2");
        sent.GetProperty("recipient_phone").GetString().ShouldBe("01712345678"); // Pathao wants the local form
        sent.GetProperty("amount_to_collect").GetDecimal().ShouldBe(1360m);
        sent.GetProperty("item_quantity").GetInt32().ShouldBe(2);
        sent.GetProperty("delivery_type").GetInt32().ShouldBe(48);
        // No destination chosen: the location fields are left out, not sent as null (as before #66).
        sent.TryGetProperty("recipient_city", out _).ShouldBeFalse();
        sent.TryGetProperty("recipient_zone", out _).ShouldBeFalse();
        sent.TryGetProperty("recipient_area", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Booking_with_a_destination_sends_pathaos_numeric_city_zone_and_area()
    {
        OrderCreated();

        await Pathao.BookAsync(Connected(), Parcel() with { Destination = new CourierDestination("1", "11", "111") }, Ct);

        var sent = JsonDocument.Parse(_pathao.LogEntries.Single().RequestMessage!.Body!).RootElement;
        sent.GetProperty("recipient_city").GetInt32().ShouldBe(1);
        sent.GetProperty("recipient_zone").GetInt32().ShouldBe(11);
        sent.GetProperty("recipient_area").GetInt32().ShouldBe(111);
    }

    private void Locations(string path, object rows) =>
        _pathao.Given(Request.Create().WithPath(path).UsingGet().WithHeader("Authorization", "Bearer acc-1"))
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                message = "List fetched",
                type = "success",
                code = 200,
                data = new { data = rows }, // Pathao wraps lists twice
            }));

    [Fact]
    public async Task Cities_and_zones_are_read_from_pathaos_nested_lists_as_text_ids()
    {
        Locations("/aladdin/api/v1/city-list", new[] { new { city_id = 1, city_name = "Dhaka" }, new { city_id = 2, city_name = " Chattogram " } });
        Locations("/aladdin/api/v1/cities/1/zone-list", new[] { new { zone_id = 11, zone_name = "Mirpur" } });

        var cities = await Pathao.GetLocationsAsync(Connected(), LocationLevel.City, null, Ct);
        var zones = await Pathao.GetLocationsAsync(Connected(), LocationLevel.Zone, "1", Ct);

        cities.ShouldBe([new CourierLocation("1", "Dhaka"), new CourierLocation("2", "Chattogram")]);
        zones.ShouldBe([new CourierLocation("11", "Mirpur")]);
    }

    [Fact]
    public async Task Areas_without_home_delivery_are_not_offered()
    {
        Locations("/aladdin/api/v1/zones/11/area-list", new object[]
        {
            new { area_id = 111, area_name = "Mirpur 10", home_delivery_available = true, pickup_available = true },
            new { area_id = 112, area_name = "Cantonment", home_delivery_available = false, pickup_available = true },
        });

        var areas = await Pathao.GetLocationsAsync(Connected(), LocationLevel.Area, "11", Ct);

        areas.ShouldBe([new CourierLocation("111", "Mirpur 10")]);
    }

    [Fact]
    public async Task A_list_pathao_answers_but_we_cannot_read_is_an_outage_not_an_empty_list()
    {
        _pathao.Given(Request.Create().WithPath("/aladdin/api/v1/city-list").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("<html>maintenance</html>"));

        await Should.ThrowAsync<CourierUnavailableException>(() => Pathao.GetLocationsAsync(Connected(), LocationLevel.City, null, Ct));
    }

    [Fact]
    public async Task A_quote_asks_pathaos_price_plan_for_the_zone()
    {
        _pathao.Given(Request.Create().WithPath("/aladdin/api/v1/merchant/price-plan").UsingPost()
                .WithBody(new JsonPartialMatcher(new { store_id = 123, recipient_city = 1, recipient_zone = 11, item_type = 2, delivery_type = 48 })))
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                message = "price",
                code = 200,
                data = new { price = 80, discount = 10, promo_discount = 0, final_price = 70, cod_enabled = 1, cod_percentage = 0.01 },
            }));

        var quote = await Pathao.QuoteAsync(Connected(), Parcel() with { Destination = new CourierDestination("1", "11") }, Ct);

        quote.ShouldBe(new CourierQuote(Money.Bdt(70m), Money.Bdt(13.60m))); // 1% of the 1,360 COD
    }

    [Fact]
    public async Task A_quote_without_a_destination_asks_for_city_and_zone()
    {
        var error = await Should.ThrowAsync<DomainValidationException>(() => Pathao.QuoteAsync(Connected(), Parcel(), Ct));

        error.Errors.ShouldContainKey("destination");
        _pathao.LogEntries.ShouldBeEmpty(); // nothing is sent
    }

    [Fact]
    public async Task A_validation_error_is_a_refusal_with_pathaos_reason()
    {
        _pathao.Given(Request.Create().WithPath(OrdersPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(422).WithBodyAsJson(new
            {
                message = "Please fix the given errors",
                type = "error",
                code = 422,
                errors = new { recipient_address = _shortAddress },
            }));

        var result = await Pathao.BookAsync(Connected(), Parcel(), Ct);

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldBe("The recipient address must be at least 10 characters.");
    }

    [Fact]
    public async Task A_duplicate_reference_is_refused_and_never_booked_blindly_again()
    {
        _pathao.Given(Request.Create().WithPath(OrdersPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(422).WithBodyAsJson(new
            {
                message = "Please fix the given errors",
                errors = new { merchant_order_id = _duplicateReference },
            }));

        var result = await Pathao.BookAsync(Connected(), Parcel(), Ct);

        result.Error!.ShouldStartWith("Pathao already has an order with this reference.");
    }

    [Fact]
    public async Task A_server_error_on_booking_is_not_retried_by_the_http_layer()
    {
        _pathao.Given(Request.Create().WithPath(OrdersPath).UsingPost()).RespondWith(Response.Create().WithStatusCode(503));

        await Should.ThrowAsync<CourierUnavailableException>(() => Pathao.BookAsync(Connected(), Parcel(), Ct));

        Calls(OrdersPath).ShouldBe(1); // the parcel may exist: our intent record decides on a retry, not Polly
    }

    [Fact]
    public async Task A_server_error_on_a_status_read_is_retried_with_backoff()
    {
        _pathao.Given(Request.Create().WithPath($"{OrdersPath}/DL1").UsingGet()).RespondWith(Response.Create().WithStatusCode(500));

        await Should.ThrowAsync<CourierUnavailableException>(() => Pathao.GetStatusAsync(Connected(), "DL1", Ct));

        Calls($"{OrdersPath}/DL1", "GET").ShouldBe(4); // first try + 3 retries (standard pipeline)
    }

    [Theory]
    [InlineData("Delivered", ParcelStatus.Delivered, null)]
    [InlineData("In_Transit", ParcelStatus.InTransit, null)]
    [InlineData("Return", ParcelStatus.Returned, null)]
    [InlineData("Partial_Delivery", ParcelStatus.Unknown, "Partial_Delivery")]
    public async Task Pathao_statuses_map_to_ours_and_unknown_ones_keep_pathaos_text(string pathao, ParcelStatus expected, string? kept)
    {
        _pathao.Given(Request.Create().WithPath($"{OrdersPath}/DL1").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                code = 200,
                data = new { consignment_id = "DL1", order_status = pathao, updated_at = "2026-11-21T09:30:00+06:00" },
            }));

        var status = await Pathao.GetStatusAsync(Connected(), "DL1", Ct);

        status.Status.ShouldBe(expected);
        status.CourierStatus.ShouldBe(kept);
        status.UpdatedAt.ShouldBe(new DateTimeOffset(2026, 11, 21, 3, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task An_expired_token_is_refreshed_once_for_parallel_calls_and_the_new_tokens_are_saved()
    {
        TokenGrant("refresh_token", 200, Tokens("acc-2", "ref-2"));
        _pathao.Given(Request.Create().WithPath(new WildcardMatcher($"{OrdersPath}/*")).UsingGet().WithHeader("Authorization", "Bearer acc-2"))
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { data = new { order_status = "Picked" } }));
        var expired = Connected(validFor: TimeSpan.FromMinutes(-1));

        var statuses = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => Pathao.GetStatusAsync(expired, $"DL{i}", Ct)));

        statuses.ShouldAllBe(s => s.Status == ParcelStatus.PickedUp);
        Calls(TokenPath).ShouldBe(1); // single-flight: one refresh, not five
        _saved.ShouldHaveSingleItem()["refreshToken"].ShouldBe("ref-2"); // rotated token kept for next time
        _saved.Single().ContainsKey("password").ShouldBeFalse();
    }

    [Fact]
    public async Task A_401_gets_a_fresh_token_and_the_call_is_sent_once_more()
    {
        _pathao.Given(Request.Create().WithPath(OrdersPath).UsingPost().WithHeader("Authorization", "Bearer acc-1"))
            .RespondWith(Response.Create().WithStatusCode(401));
        TokenGrant("refresh_token", 200, Tokens("acc-2", "ref-2"));
        OrderCreated(token: "acc-2");

        var result = await Pathao.BookAsync(Connected(), Parcel(), Ct);

        result.Succeeded.ShouldBeTrue();
        Calls(OrdersPath).ShouldBe(2); // safe: Pathao did nothing with the 401
    }

    [Fact]
    public async Task A_refused_refresh_asks_the_seller_to_reconnect()
    {
        TokenGrant("refresh_token", 401);

        await Should.ThrowAsync<CourierAuthExpiredException>(() => Pathao.GetStatusAsync(Connected(validFor: TimeSpan.Zero), "DL1", Ct));

        _saved.ShouldBeEmpty();
    }
}
