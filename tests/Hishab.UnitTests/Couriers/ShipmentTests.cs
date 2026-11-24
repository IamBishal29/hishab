using Hishab.Domain.Common;
using Hishab.Domain.Couriers;

namespace Hishab.UnitTests.Couriers;

public class ShipmentTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 21, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid _orderId = Guid.NewGuid();

    [Fact]
    public void Every_attempt_for_an_order_sends_the_same_reference()
    {
        var first = Shipment.Request(_orderId, CourierProvider.Fake, _now);
        var later = Shipment.Request(_orderId, CourierProvider.Steadfast, _now.AddHours(1));

        first.MerchantReference.ShouldBe(_orderId.ToString("N"));
        later.MerchantReference.ShouldBe(first.MerchantReference);
        first.State.ShouldBe(ShipmentState.Requested);
    }

    [Fact]
    public void A_requested_shipment_becomes_booked_with_the_couriers_ids()
    {
        var shipment = Shipment.Request(_orderId, CourierProvider.Fake, _now);

        shipment.MarkBooked("FAKE-0123456789", "FAKE0123456789", Money.Bdt(60m), _now.AddSeconds(2));

        shipment.State.ShouldBe(ShipmentState.Booked);
        shipment.ConsignmentId.ShouldBe("FAKE-0123456789");
        shipment.DeliveryFee.ShouldBe(Money.Bdt(60m));
        shipment.BookedAt.ShouldBe(_now.AddSeconds(2));
    }

    [Fact]
    public void A_rejected_shipment_can_be_requested_again_but_a_booked_one_cannot()
    {
        var shipment = Shipment.Request(_orderId, CourierProvider.Fake, _now);
        shipment.MarkRejected("Recipient unreachable.");

        shipment.RequestAgain(_now.AddMinutes(5));

        shipment.State.ShouldBe(ShipmentState.Requested);
        shipment.Error.ShouldBeNull();
        shipment.MarkBooked("FAKE-0123456789", null, null, _now.AddMinutes(6));
        Should.Throw<ConflictException>(() => shipment.RequestAgain(_now.AddMinutes(7)));
    }

    [Fact]
    public void Requesting_again_while_the_outcome_is_unknown_keeps_the_same_attempt()
    {
        var shipment = Shipment.Request(_orderId, CourierProvider.Fake, _now);

        shipment.RequestAgain(_now.AddMinutes(1)); // the courier may already have the parcel

        shipment.State.ShouldBe(ShipmentState.Requested);
        shipment.RequestedAt.ShouldBe(_now);
    }

    [Fact]
    public void Only_a_requested_shipment_takes_an_outcome()
    {
        var shipment = Shipment.Request(_orderId, CourierProvider.Fake, _now);
        shipment.MarkBooked("FAKE-0123456789", null, null, _now);

        Should.Throw<ConflictException>(() => shipment.MarkBooked("FAKE-9999999999", null, null, _now));
        Should.Throw<ConflictException>(() => shipment.MarkRejected("late refusal"));
    }

    [Fact]
    public void A_long_courier_error_is_cut_to_fit()
    {
        var shipment = Shipment.Request(_orderId, CourierProvider.Fake, _now);

        shipment.MarkRejected(new string('x', 1_000));

        shipment.Error!.Length.ShouldBe(Shipment.ErrorMaxLength);
    }

    [Fact]
    public void A_booked_parcel_takes_a_return_charge_and_a_correction_replaces_it()
    {
        var shipment = Shipment.Request(_orderId, CourierProvider.Fake, _now);
        shipment.MarkBooked("CN-1", null, Money.Bdt(60m), _now);

        shipment.RecordReturnCharge(Money.Bdt(60m));
        shipment.RecordReturnCharge(Money.Bdt(30m));

        shipment.ReturnCharge.ShouldBe(Money.Bdt(30m));
    }

    [Fact]
    public void A_parcel_that_never_left_has_no_return_charge()
    {
        var shipment = Shipment.Request(_orderId, CourierProvider.Fake, _now);

        Should.Throw<ConflictException>(() => shipment.RecordReturnCharge(Money.Bdt(60m)));
    }

    [Fact]
    public void A_negative_return_charge_is_rejected_on_its_field()
    {
        var shipment = Shipment.Request(_orderId, CourierProvider.Fake, _now);
        shipment.MarkBooked("CN-1", null, null, _now);

        Should.Throw<DomainValidationException>(() => shipment.RecordReturnCharge(Money.Bdt(-1m))).Errors.Keys.ShouldBe(["returnCharge"]);
    }
}
