using Hishab.Domain.Common;
using Hishab.Domain.Couriers;

namespace Hishab.UnitTests.Couriers;

public class CourierDestinationTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 22, 4, 0, 0, TimeSpan.Zero);
    private static readonly CourierDestination _mirpur = new("1", "11", "111");
    private static readonly CourierDestination _dhanmondi = new("1", "12");

    [Theory]
    [InlineData("", "11", "destination.cityId")]
    [InlineData("1", " ", "destination.zoneId")]
    [InlineData("1 2", "11", "destination.cityId")]
    [InlineData("1", "123456789012345678901234567890123", "destination.zoneId")]
    public void A_destination_needs_a_city_and_zone_with_plain_ids(string city, string zone, string field)
    {
        var error = Should.Throw<DomainValidationException>(() => new CourierDestination(city, zone));

        error.Errors.ShouldContainKey(field);
    }

    [Fact]
    public void A_blank_area_means_no_area()
    {
        new CourierDestination("1", "11", "  ").AreaId.ShouldBeNull();
    }

    [Fact]
    public void A_shipment_keeps_the_destination_it_was_requested_with()
    {
        Shipment.Request(Guid.NewGuid(), CourierProvider.Pathao, _now, _mirpur).Destination.ShouldBe(_mirpur);
        Shipment.Request(Guid.NewGuid(), CourierProvider.Pathao, _now).Destination.ShouldBeNull();
    }

    [Fact]
    public void After_a_rejection_a_new_destination_replaces_the_old_one()
    {
        var shipment = Rejected(_mirpur);

        shipment.RequestAgain(_now.AddMinutes(5), _dhanmondi);

        shipment.State.ShouldBe(ShipmentState.Requested);
        shipment.Destination.ShouldBe(_dhanmondi);
    }

    [Fact]
    public void A_retry_without_a_destination_keeps_the_saved_one()
    {
        var rejected = Rejected(_mirpur);
        var unfinished = Shipment.Request(Guid.NewGuid(), CourierProvider.Pathao, _now, _mirpur);

        rejected.RequestAgain(_now.AddMinutes(5));
        unfinished.RequestAgain(_now.AddMinutes(5));

        rejected.Destination.ShouldBe(_mirpur);
        unfinished.Destination.ShouldBe(_mirpur);
    }

    [Fact]
    public void An_unfinished_attempt_cannot_be_sent_somewhere_else()
    {
        // The courier may already hold a parcel for Mirpur: re-sending it to Dhanmondi could make two.
        var unfinished = Shipment.Request(Guid.NewGuid(), CourierProvider.Pathao, _now, _mirpur);

        Should.Throw<ConflictException>(() => unfinished.RequestAgain(_now.AddMinutes(5), _dhanmondi));
        unfinished.RequestAgain(_now.AddMinutes(5), _mirpur); // the same place is a plain retry
        unfinished.Destination.ShouldBe(_mirpur);
    }

    private static Shipment Rejected(CourierDestination destination)
    {
        var shipment = Shipment.Request(Guid.NewGuid(), CourierProvider.Pathao, _now, destination);
        shipment.MarkRejected("Wrong zone for this address.");
        return shipment;
    }
}
