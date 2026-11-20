using Hishab.Domain.Common;
using Hishab.Domain.Orders;

namespace Hishab.UnitTests.Orders;

public class OrderLifecycleTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    /// <summary>The PRD diagram, written out by hand: the table in the code must match it exactly.</summary>
    private static readonly (OrderStatus From, OrderStatus To)[] _allowed =
    [
        (OrderStatus.New, OrderStatus.Confirmed),
        (OrderStatus.New, OrderStatus.Cancelled),
        (OrderStatus.Confirmed, OrderStatus.Cancelled),
        (OrderStatus.Confirmed, OrderStatus.Booked),
        (OrderStatus.Booked, OrderStatus.InTransit),
        (OrderStatus.InTransit, OrderStatus.Delivered),
        (OrderStatus.InTransit, OrderStatus.Returned),
        (OrderStatus.Delivered, OrderStatus.CodSettled),
        (OrderStatus.Returned, OrderStatus.ReceivedBack),
    ];

    public static TheoryData<OrderStatus, OrderStatus> Allowed => [.. _allowed];

    public static TheoryData<OrderStatus, OrderStatus> Forbidden =>
    [
        .. from f in Enum.GetValues<OrderStatus>()
           from t in Enum.GetValues<OrderStatus>()
           where !_allowed.Contains((f, t))
           select (f, t),
    ];

    /// <summary>The shortest path from New to <paramref name="status"/> along allowed moves.</summary>
    private static readonly Dictionary<OrderStatus, OrderStatus[]> _pathTo = new()
    {
        [OrderStatus.New] = [],
        [OrderStatus.Confirmed] = [OrderStatus.Confirmed],
        [OrderStatus.Cancelled] = [OrderStatus.Cancelled],
        [OrderStatus.Booked] = [OrderStatus.Confirmed, OrderStatus.Booked],
        [OrderStatus.InTransit] = [OrderStatus.Confirmed, OrderStatus.Booked, OrderStatus.InTransit],
        [OrderStatus.Delivered] = [OrderStatus.Confirmed, OrderStatus.Booked, OrderStatus.InTransit, OrderStatus.Delivered],
        [OrderStatus.Returned] = [OrderStatus.Confirmed, OrderStatus.Booked, OrderStatus.InTransit, OrderStatus.Returned],
        [OrderStatus.CodSettled] = [OrderStatus.Confirmed, OrderStatus.Booked, OrderStatus.InTransit, OrderStatus.Delivered, OrderStatus.CodSettled],
        [OrderStatus.ReceivedBack] = [OrderStatus.Confirmed, OrderStatus.Booked, OrderStatus.InTransit, OrderStatus.Returned, OrderStatus.ReceivedBack],
    };

    internal static Order OrderIn(OrderStatus status)
    {
        var order = OrderTests.NewOrder();
        foreach (var step in _pathTo[status])
        {
            order.TransitionTo(step, Guid.NewGuid(), _now);
        }

        order.Status.ShouldBe(status);
        return order;
    }

    [Theory]
    [MemberData(nameof(Allowed))]
    public void Allowed_transition_moves_the_order_and_records_it(OrderStatus from, OrderStatus to)
    {
        var order = OrderIn(from);
        var packer = Guid.NewGuid();

        order.TransitionTo(to, packer, _now.AddHours(1), "  checked by phone ");

        order.Status.ShouldBe(to);
        var last = order.History[^1];
        last.From.ShouldBe(from);
        last.To.ShouldBe(to);
        last.ChangedBy.ShouldBe(packer);
        last.ChangedAt.ShouldBe(_now.AddHours(1));
        last.Note.ShouldBe("checked by phone");
    }

    [Theory]
    [MemberData(nameof(Forbidden))]
    public void Forbidden_transition_throws_and_changes_nothing(OrderStatus from, OrderStatus to)
    {
        var order = OrderIn(from);
        var historyBefore = order.History.Count;

        var error = Should.Throw<ConflictException>(() => order.TransitionTo(to, Guid.NewGuid(), _now));

        error.Message.ShouldBe($"An order cannot move from {from} to {to}.");
        order.Status.ShouldBe(from);
        order.History.Count.ShouldBe(historyBefore);
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.CodSettled)]
    [InlineData(OrderStatus.ReceivedBack)]
    public void End_states_are_final(OrderStatus status)
    {
        OrderLifecycle.IsFinal(status).ShouldBeTrue();
    }
}

public class OrderTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private static OrderLineDetails Line(decimal price, int quantity, decimal? cost = null, string name = "Polo shirt") =>
        new(Guid.NewGuid(), name, "M / Red", "POLO-M-RED", Money.Bdt(price), cost is { } c ? Money.Bdt(c) : null, quantity);

    internal static Order NewOrder(decimal delivery = 60m, decimal advance = 0m, params OrderLineDetails[] lines) =>
        Order.Create(
            Guid.NewGuid(),
            "House 4, Road 2, Mirpur",
            isInsideDhaka: true,
            OrderSource.Facebook,
            lines.Length > 0 ? lines : [Line(650m, 2)],
            Money.Bdt(delivery),
            Money.Bdt(advance),
            note: null,
            createdBy: Guid.NewGuid(),
            _now);

    [Fact]
    public void Totals_and_cash_on_delivery_are_computed_from_the_lines()
    {
        var order = NewOrder(delivery: 60m, advance: 100m, Line(650m, 2), Line(299.50m, 1, name: "Cap"));

        order.Subtotal.ShouldBe(Money.Bdt(1_599.50m));
        order.Total.ShouldBe(Money.Bdt(1_659.50m));
        order.CodAmount.ShouldBe(Money.Bdt(1_559.50m)); // the courier collects the rest
    }

    [Fact]
    public void A_new_order_starts_as_New_with_its_creation_in_the_history()
    {
        var order = NewOrder();

        order.Status.ShouldBe(OrderStatus.New);
        var created = order.History.ShouldHaveSingleItem();
        created.From.ShouldBeNull();
        created.To.ShouldBe(OrderStatus.New);
    }

    [Fact]
    public void Lines_keep_the_price_and_cost_from_order_time()
    {
        var order = NewOrder(lines: Line(650m, 1, cost: 400m));

        order.Lines[0].UnitPrice.ShouldBe(Money.Bdt(650m));
        order.Lines[0].UnitCost.ShouldBe(Money.Bdt(400m));
        order.Lines[0].Sku.ShouldBe("POLO-M-RED");
    }

    [Fact]
    public void Advance_cannot_exceed_the_total_or_be_negative()
    {
        Should.Throw<DomainValidationException>(() => NewOrder(delivery: 60m, advance: 1_361m, Line(650m, 2)))
            .Errors.Keys.ShouldBe(["advancePaid"]);
        Should.Throw<DomainValidationException>(() => NewOrder(advance: -1m))
            .Errors.Keys.ShouldBe(["advancePaid"]);
    }

    [Fact]
    public void Paying_everything_in_advance_leaves_nothing_to_collect()
    {
        NewOrder(delivery: 60m, advance: 1_360m, Line(650m, 2)).CodAmount.ShouldBe(Money.Zero);
    }

    [Fact]
    public void An_order_needs_lines_with_positive_quantities()
    {
        Should.Throw<DomainValidationException>(() =>
                Order.Create(Guid.NewGuid(), null, true, OrderSource.Phone, [], Money.Zero, Money.Zero, null, null, _now))
            .Errors.Keys.ShouldBe(["lines"]);

        Should.Throw<DomainValidationException>(() => NewOrder(lines: Line(650m, 0)))
            .Errors.Keys.ShouldBe(["lines[0].quantity"]);
    }
}
