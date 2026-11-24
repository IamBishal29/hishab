using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Finance;
using Hishab.Domain.Orders;
using Shouldly;

namespace Hishab.UnitTests.Finance;

public class PayoutTests
{
    // 24 Nov 2026, 10:00 in Dhaka.
    private static readonly DateTimeOffset _now = new(2026, 11, 24, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly _today = new(2026, 11, 24);

    private static PayoutLineInput Line(string consignment, decimal collected, decimal deliveryFee = 60m, decimal codFee = 0m) =>
        new(consignment, Money.Bdt(collected), Money.Bdt(deliveryFee), Money.Bdt(codFee));

    private static PayoutParcel Parcel(decimal due, OrderStatus status = OrderStatus.Delivered) =>
        new(Guid.NewGuid(), Guid.NewGuid(), status, Money.Bdt(due));

    private static Payout Record(IReadOnlyList<PayoutLineInput> lines, Dictionary<string, PayoutParcel> parcels, decimal net, DateOnly? paidOn = null) =>
        Payout.Record(CourierProvider.Pathao, " PTH-1124 ", paidOn ?? _today, Money.Bdt(net), null, lines, parcels, by: null, _now);

    [Fact]
    public void Matching_rules_apply_in_order_repeat_unknown_already_paid_not_delivered_amount()
    {
        var delivered = Parcel(1000m);
        PayoutMatching.Decide(Line("A", 1000m), delivered, seenEarlier: true).ShouldBe(PayoutLineMatch.RepeatedInPayout);
        PayoutMatching.Decide(Line("A", 1000m), parcel: null, seenEarlier: false).ShouldBe(PayoutLineMatch.UnknownParcel);
        PayoutMatching.Decide(Line("A", 1000m), Parcel(1000m, OrderStatus.CodSettled), false).ShouldBe(PayoutLineMatch.AlreadyPaid);
        PayoutMatching.Decide(Line("A", 1000m), Parcel(1000m, OrderStatus.InTransit), false).ShouldBe(PayoutLineMatch.NotDelivered);
        PayoutMatching.Decide(Line("A", 950m), delivered, false).ShouldBe(PayoutLineMatch.AmountDiffers);
        PayoutMatching.Decide(Line("A", 1000m), delivered, false).ShouldBe(PayoutLineMatch.Settled);
    }

    [Theory]
    [InlineData(OrderStatus.Booked)]
    [InlineData(OrderStatus.InTransit)]
    [InlineData(OrderStatus.Returned)]
    [InlineData(OrderStatus.ReceivedBack)]
    public void A_parcel_whose_order_is_not_delivered_settles_nothing(OrderStatus status)
    {
        var payout = Record([Line("A", 500m)], new() { ["A"] = Parcel(500m, status) }, net: 440m);

        var line = payout.Lines.ShouldHaveSingleItem();
        line.Match.ShouldBe(PayoutLineMatch.NotDelivered);
        line.SettlesOrderId.ShouldBeNull();
        line.OrderId.ShouldNotBeNull(); // still linked, so staff can see which order it was
    }

    [Fact]
    public void Short_paid_parcel_is_settled_and_its_shortfall_kept()
    {
        var parcel = Parcel(1000m);
        var payout = Record([Line("A", 950m)], new() { ["A"] = parcel }, net: 890m);

        var line = payout.Lines.ShouldHaveSingleItem();
        line.Match.ShouldBe(PayoutLineMatch.AmountDiffers);
        line.SettlesOrderId.ShouldBe(parcel.OrderId);
        line.SettledShipmentId.ShouldBe(parcel.ShipmentId);
        line.Shortfall.ShouldBe(Money.Bdt(50m));
    }

    [Fact]
    public void A_consignment_listed_twice_settles_its_order_once()
    {
        var payout = Record([Line("A", 500m), Line(" A ", 500m)], new() { ["A"] = Parcel(500m) }, net: 880m);

        payout.Lines.Select(l => l.Match).ShouldBe([PayoutLineMatch.Settled, PayoutLineMatch.RepeatedInPayout]);
        payout.Lines.Count(l => l.SettlesOrderId is not null).ShouldBe(1);
    }

    [Fact]
    public void Books_balance_net_equals_collected_minus_fees_plus_an_explicit_difference()
    {
        var payout = Record(
            [Line("A", 1200m, deliveryFee: 60m, codFee: 12m), Line("B", 3800m, deliveryFee: 120m, codFee: 38m), Line("X", 500m)],
            new() { ["A"] = Parcel(1200m), ["B"] = Parcel(3800m) },
            net: 5200m);

        payout.Collected.ShouldBe(Money.Bdt(5500m)); // unknown lines still count: the courier says it paid them
        payout.DeliveryFees.ShouldBe(Money.Bdt(240m));
        payout.CodFees.ShouldBe(Money.Bdt(50m));
        payout.StatementNet.ShouldBe(Money.Bdt(5210m));
        payout.Difference.ShouldBe(Money.Bdt(-10m)); // ৳10 missing: shown, never rounded away
        (payout.StatementNet + payout.Difference).ShouldBe(payout.NetAmount);
        payout.Reference.ShouldBe("PTH-1124");
        payout.Lines.Select(l => l.Sequence).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Payout_dated_in_the_future_is_rejected()
    {
        var error = Should.Throw<DomainValidationException>(() => Record([Line("A", 1m)], [], net: 0m, paidOn: _today.AddDays(1)));
        error.Errors.Keys.ShouldBe(["paidOn"]);
    }

    [Fact]
    public void Negative_line_amounts_are_rejected_with_the_line_field()
    {
        var error = Should.Throw<DomainValidationException>(() => Record([Line("A", 1m), Line("B", 1m, codFee: -1m)], [], net: 0m));
        error.Errors.Keys.ShouldBe(["lines[1].codFee"]);
    }

    [Fact]
    public void Payout_without_lines_is_rejected()
    {
        Should.Throw<DomainValidationException>(() => Record([], [], net: 0m)).Errors.Keys.ShouldBe(["lines"]);
    }

    [Fact]
    public void Rematch_settles_a_parcel_that_was_in_transit_on_the_statement_day()
    {
        var parcel = Parcel(500m, OrderStatus.InTransit);
        var payout = Record([Line("A", 500m), Line("B", 300m)], new() { ["A"] = parcel }, net: 680m);
        payout.Lines.Select(l => l.Match).ShouldBe([PayoutLineMatch.NotDelivered, PayoutLineMatch.UnknownParcel]);
        var differenceBefore = payout.Difference;

        var b = Parcel(300m); // booked under our records after the statement (a lost booking answer)
        var settled = payout.Rematch(new Dictionary<string, PayoutParcel> { ["A"] = parcel with { OrderStatus = OrderStatus.Delivered }, ["B"] = b });

        settled.Select(l => l.SettlesOrderId).ShouldBe([parcel.OrderId, b.OrderId]);
        payout.Lines.Select(l => l.Match).ShouldBe([PayoutLineMatch.Settled, PayoutLineMatch.Settled]);
        payout.Difference.ShouldBe(differenceBefore); // re-matching never touches an amount
    }

    [Fact]
    public void Rematch_leaves_settled_paid_and_repeated_lines_alone()
    {
        var settledParcel = Parcel(500m);
        var paidElsewhere = Parcel(200m, OrderStatus.CodSettled);
        var payout = Record(
            [Line("A", 500m), Line("A", 500m), Line("P", 200m)],
            new() { ["A"] = settledParcel, ["P"] = paidElsewhere },
            net: 1080m);

        // Even if the world changed, final lines keep their result and settle nothing new.
        var settled = payout.Rematch(new Dictionary<string, PayoutParcel>
        {
            ["A"] = settledParcel with { OrderStatus = OrderStatus.CodSettled },
            ["P"] = paidElsewhere with { OrderStatus = OrderStatus.Delivered },
        });

        settled.ShouldBeEmpty();
        payout.Lines.Select(l => l.Match).ShouldBe([PayoutLineMatch.Settled, PayoutLineMatch.RepeatedInPayout, PayoutLineMatch.AlreadyPaid]);
        payout.Lines[0].SettlesOrderId.ShouldBe(settledParcel.OrderId);
    }

    [Fact]
    public void Rematch_records_why_a_line_still_settles_nothing()
    {
        var returned = Parcel(500m, OrderStatus.InTransit);
        var payout = Record([Line("A", 500m)], new() { ["A"] = returned }, net: 440m);

        var settled = payout.Rematch(new Dictionary<string, PayoutParcel> { ["A"] = returned with { OrderStatus = OrderStatus.CodSettled } });

        settled.ShouldBeEmpty();
        var line = payout.Lines.ShouldHaveSingleItem();
        line.Match.ShouldBe(PayoutLineMatch.AlreadyPaid); // another payout paid it meanwhile: never twice
        line.SettlesOrderId.ShouldBeNull();
    }
}
