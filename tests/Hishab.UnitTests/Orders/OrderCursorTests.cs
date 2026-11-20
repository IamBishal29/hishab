using Hishab.Application.Orders;
using Hishab.Domain.Common;

namespace Hishab.UnitTests.Orders;

public class OrderCursorTests
{
    [Fact]
    public void Cursor_round_trips_the_bookmark()
    {
        var bookmark = new OrderCursor(new DateTimeOffset(2026, 11, 20, 10, 15, 30, 123, TimeSpan.Zero).AddTicks(4567), 1043);

        var encoded = bookmark.Encode();

        OrderCursor.TryDecode(encoded, out var decoded).ShouldBeTrue();
        decoded.ShouldBe(bookmark);
        encoded.ShouldNotContain("="); // URL-safe: no padding, no '+' or '/'
        encoded.ShouldNotContain("+");
        encoded.ShouldNotContain("/");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-cursor")]
    [InlineData("AAAA")]
    [InlineData("____________________________________________________________________")]
    public void Garbage_is_not_a_cursor(string? value) =>
        OrderCursor.TryDecode(value, out _).ShouldBeFalse();

    [Fact]
    public void Dhaka_day_starts_at_18_00_utc_the_day_before()
    {
        DhakaTime.StartOfDay(new DateOnly(2026, 11, 20)).ShouldBe(new DateTimeOffset(2026, 11, 19, 18, 0, 0, TimeSpan.Zero));
        DhakaTime.DayOf(new DateTimeOffset(2026, 11, 19, 18, 0, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 11, 20));
        DhakaTime.DayOf(new DateTimeOffset(2026, 11, 19, 17, 59, 59, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 11, 19));
    }
}
