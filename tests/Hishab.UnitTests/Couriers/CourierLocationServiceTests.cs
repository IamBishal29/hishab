using Hishab.Application.Couriers;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Infrastructure.Couriers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Hishab.UnitTests.Couriers;

public sealed class CourierLocationServiceTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CourierCredentials _keys = new(new Dictionary<string, string> { ["apiKey"] = "k" });

    private readonly ICourierProvider _courier = Substitute.For<ICourierProvider>();
    private readonly ICourierAccountService _accounts = Substitute.For<ICourierAccountService>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly CourierLocationService _service;

    public CourierLocationServiceTests()
    {
        _courier.Code.Returns(CourierProvider.Pathao);
        _courier.SupportsLocations.Returns(true);
        var registry = Substitute.For<ICourierProviderRegistry>();
        registry.Find(CourierProvider.Pathao).Returns(_courier);
        _accounts.GetCredentialsAsync(CourierProvider.Pathao, Arg.Any<CancellationToken>()).Returns(_keys);
        Lists(LocationLevel.City, null, ("2", "Chattogram"), ("1", "Dhaka"));
        Lists(LocationLevel.Zone, "1", ("11", "Mirpur"));
        Lists(LocationLevel.Area, "11", ("111", "Mirpur 10"));
        _service = new CourierLocationService(registry, _accounts, _cache, NullLogger<CourierLocationService>.Instance);
    }

    public void Dispose() => _cache.Dispose();

    private void Lists(LocationLevel level, string? parent, params (string Id, string Name)[] rows) =>
        _courier.GetLocationsAsync(_keys, level, parent, Arg.Any<CancellationToken>())
            .Returns([.. rows.Select(r => new CourierLocation(r.Id, r.Name))]);

    private Task<IReadOnlyList<CourierLocation>> CourierCalls(LocationLevel level, string? parent) =>
        _courier.Received().GetLocationsAsync(_keys, level, parent, Arg.Any<CancellationToken>());

    [Fact]
    public async Task A_list_is_fetched_once_and_served_from_the_cache_sorted_by_name()
    {
        var first = await _service.ListAsync(CourierProvider.Pathao, LocationLevel.City, null, Ct);
        var second = await _service.ListAsync(CourierProvider.Pathao, LocationLevel.City, null, Ct);

        first.Select(c => c.Name).ShouldBe(["Chattogram", "Dhaka"]);
        second.ShouldBeSameAs(first);
        await _courier.Received(1).GetLocationsAsync(_keys, LocationLevel.City, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_empty_list_is_not_cached()
    {
        Lists(LocationLevel.Area, "999");

        await _service.ListAsync(CourierProvider.Pathao, LocationLevel.Area, "999", Ct);
        await _service.ListAsync(CourierProvider.Pathao, LocationLevel.Area, "999", Ct);

        await _courier.Received(2).GetLocationsAsync(_keys, LocationLevel.Area, "999", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Zones_are_only_fetched_for_a_city_the_courier_listed()
    {
        var error = await Should.ThrowAsync<DomainValidationException>(
            () => _service.ListAsync(CourierProvider.Pathao, LocationLevel.Zone, "42", Ct));

        error.Errors.ShouldContainKey("parentId");
        await _courier.DidNotReceive().GetLocationsAsync(_keys, LocationLevel.Zone, "42", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(LocationLevel.City, "1")]
    [InlineData(LocationLevel.Zone, null)]
    [InlineData(LocationLevel.Area, " ")]
    public async Task Each_level_has_exactly_the_parent_it_needs(LocationLevel level, string? parent)
    {
        var error = await Should.ThrowAsync<DomainValidationException>(() => _service.ListAsync(CourierProvider.Pathao, level, parent, Ct));

        error.Errors.ShouldContainKey("parentId");
    }

    [Fact]
    public async Task A_courier_without_location_lists_is_refused()
    {
        _courier.SupportsLocations.Returns(false);

        var error = await Should.ThrowAsync<DomainValidationException>(
            () => _service.ListAsync(CourierProvider.Pathao, LocationLevel.City, null, Ct));

        error.Errors.ShouldContainKey("provider");
    }

    [Fact]
    public async Task A_courier_that_is_not_connected_is_refused_with_a_hint()
    {
        _accounts.GetCredentialsAsync(CourierProvider.Pathao, Arg.Any<CancellationToken>())
            .Returns<CourierCredentials>(_ => throw new NotFoundException("Courier", CourierProvider.Pathao));

        var error = await Should.ThrowAsync<DomainValidationException>(
            () => _service.ListAsync(CourierProvider.Pathao, LocationLevel.City, null, Ct));

        error.Errors["provider"][0].ShouldContain("Connect Pathao");
    }

    [Theory]
    [InlineData("1", "11", "111", null)]
    [InlineData("1", "11", null, null)]
    [InlineData("1", "12", null, "destination.zoneId")]
    [InlineData("9", "11", null, "destination.cityId")]
    [InlineData("1", "11", "999", "destination.areaId")]
    public async Task A_destination_must_follow_the_couriers_own_tree(string city, string zone, string? area, string? badField)
    {
        var check = _service.EnsureValidAsync(CourierProvider.Pathao, new CourierDestination(city, zone, area), Ct);

        if (badField is null)
        {
            await check;
            await CourierCalls(LocationLevel.Zone, city);
        }
        else
        {
            (await Should.ThrowAsync<DomainValidationException>(() => check)).Errors.ShouldContainKey(badField);
        }
    }
}
