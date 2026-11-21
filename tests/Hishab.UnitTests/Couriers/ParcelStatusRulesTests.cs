using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Orders;

namespace Hishab.UnitTests.Couriers;

public class ParcelStatusRulesTests
{
    [Theory]
    [InlineData(OrderStatus.Booked, ParcelStatus.PickedUp, new[] { OrderStatus.InTransit })]
    [InlineData(OrderStatus.Booked, ParcelStatus.InTransit, new[] { OrderStatus.InTransit })]
    [InlineData(OrderStatus.Booked, ParcelStatus.Delivered, new[] { OrderStatus.InTransit, OrderStatus.Delivered })]
    [InlineData(OrderStatus.Booked, ParcelStatus.Returned, new[] { OrderStatus.InTransit, OrderStatus.Returned })]
    [InlineData(OrderStatus.InTransit, ParcelStatus.Delivered, new[] { OrderStatus.Delivered })]
    [InlineData(OrderStatus.InTransit, ParcelStatus.Returned, new[] { OrderStatus.Returned })]
    public void A_newer_courier_status_moves_the_order_through_every_step(OrderStatus order, ParcelStatus parcel, OrderStatus[] moves)
    {
        var decision = ParcelStatusRules.Decide(order, parcel);

        decision.Moves.ShouldBe(moves);
        decision.NeedsReview.ShouldBeFalse();
    }

    [Theory]
    [InlineData(OrderStatus.Booked, ParcelStatus.Pending)]
    [InlineData(OrderStatus.InTransit, ParcelStatus.InTransit)] // the same status twice (a repeated sync)
    [InlineData(OrderStatus.InTransit, ParcelStatus.Pending)] // an older status arriving late
    [InlineData(OrderStatus.Delivered, ParcelStatus.Delivered)]
    [InlineData(OrderStatus.Delivered, ParcelStatus.InTransit)]
    [InlineData(OrderStatus.CodSettled, ParcelStatus.Delivered)] // the shop has moved on: the courier never moves it back
    [InlineData(OrderStatus.ReceivedBack, ParcelStatus.Returned)]
    public void A_status_the_order_already_reached_changes_nothing(OrderStatus order, ParcelStatus parcel)
    {
        ParcelStatusRules.Decide(order, parcel).ShouldBe(ParcelStatusDecision.Nothing);
    }

    [Theory]
    [InlineData(OrderStatus.Delivered, ParcelStatus.Returned)]
    [InlineData(OrderStatus.Returned, ParcelStatus.Delivered)]
    [InlineData(OrderStatus.Booked, ParcelStatus.Cancelled)] // no Booked -> Cancelled move in the PRD lifecycle
    [InlineData(OrderStatus.InTransit, ParcelStatus.Unknown)]
    public void A_status_the_order_cannot_follow_is_flagged_for_review_without_moves(OrderStatus order, ParcelStatus parcel)
    {
        ParcelStatusRules.Decide(order, parcel).ShouldBe(ParcelStatusDecision.Review);
    }

    [Fact]
    public void Every_move_the_rules_produce_is_allowed_by_the_lifecycle()
    {
        foreach (var order in Enum.GetValues<OrderStatus>())
        {
            foreach (var parcel in Enum.GetValues<ParcelStatus>())
            {
                var from = order;
                foreach (var to in ParcelStatusRules.Decide(order, parcel).Moves)
                {
                    OrderLifecycle.CanMove(from, to).ShouldBeTrue($"{order} + {parcel}: {from} -> {to}");
                    from = to;
                }
            }
        }
    }

    private static Shipment Booked()
    {
        var shipment = Shipment.Request(Guid.NewGuid(), CourierProvider.Fake, DateTimeOffset.UtcNow);
        shipment.MarkBooked("FAKE-1", "T1", null, DateTimeOffset.UtcNow);
        return shipment;
    }

    [Fact]
    public void A_shipment_stops_tracking_once_the_courier_reports_a_final_status()
    {
        var shipment = Booked();
        shipment.RecordStatus(ParcelStatus.InTransit, null, null, DateTimeOffset.UtcNow);
        shipment.IsTracking.ShouldBeTrue();

        shipment.RecordStatus(ParcelStatus.Delivered, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        shipment.IsTracking.ShouldBeFalse();
    }

    [Fact]
    public void An_unknown_status_keeps_the_couriers_text_and_keeps_tracking()
    {
        var shipment = Booked();

        shipment.RecordStatus(ParcelStatus.Unknown, "hold_at_hub_" + new string('x', 100), null, DateTimeOffset.UtcNow);

        shipment.CourierStatus!.Length.ShouldBe(Shipment.ConsignmentMaxLength);
        shipment.IsTracking.ShouldBeTrue();
    }

    [Fact]
    public void Only_booked_shipments_have_a_courier_status()
    {
        var requested = Shipment.Request(Guid.NewGuid(), CourierProvider.Fake, DateTimeOffset.UtcNow);

        Should.Throw<ConflictException>(() => requested.RecordStatus(ParcelStatus.InTransit, null, null, DateTimeOffset.UtcNow));
    }
}
