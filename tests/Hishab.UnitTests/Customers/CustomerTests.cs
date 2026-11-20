using Hishab.Domain.Common;
using Hishab.Domain.Customers;

namespace Hishab.UnitTests.Customers;

public class CustomerTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("01712345678")]
    [InlineData("+880 1712-345678")]
    [InlineData("8801712345678")]
    public void Every_spelling_of_a_number_gives_the_same_natural_key(string typed)
    {
        Customer.Create(Phone.Parse(typed), null, null, _now).Phone.ShouldBe("+8801712345678");
    }

    [Fact]
    public void Fill_missing_keeps_known_details_and_fills_gaps()
    {
        var customer = Customer.Create(Phone.Parse("01712345678"), "Rahima Begum", null, _now);

        customer.FillMissing("rahima", "House 4, Road 2, Mirpur").ShouldBeTrue();

        customer.Name.ShouldBe("Rahima Begum");
        customer.Address.ShouldBe("House 4, Road 2, Mirpur");
    }

    [Fact]
    public void Fill_missing_reports_no_change_when_nothing_is_new()
    {
        var customer = Customer.Create(Phone.Parse("01712345678"), "Rahima", "Mirpur", _now);

        customer.FillMissing("Someone else", "  ").ShouldBeFalse();
    }

    [Fact]
    public void Block_records_reason_and_time_and_unblock_clears_them()
    {
        var customer = Customer.Create(Phone.Parse("01712345678"), null, null, _now);

        customer.Block("  Refused 3 parcels ", _now);
        customer.IsBlocked.ShouldBeTrue();
        customer.BlockedReason.ShouldBe("Refused 3 parcels");
        customer.BlockedAt.ShouldBe(_now);

        customer.Unblock();
        customer.IsBlocked.ShouldBeFalse();
        customer.BlockedReason.ShouldBeNull();
        customer.BlockedAt.ShouldBeNull();
    }

    [Fact]
    public void Overlong_details_are_rejected_per_field()
    {
        var customer = Customer.Create(Phone.Parse("01712345678"), null, null, _now);

        Should.Throw<DomainValidationException>(() =>
                customer.UpdateDetails(new string('n', 101), new string('a', 301), new string('x', 1_001)))
            .Errors.Keys.ShouldBe(["name", "address", "notes"], ignoreOrder: true);
    }
}
