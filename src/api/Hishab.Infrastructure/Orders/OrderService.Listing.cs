using Hishab.Application.Orders;
using Hishab.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Hishab.Infrastructure.Orders;

internal sealed partial class OrderService
{
    public const string InvalidCursorMessage = "This cursor is not valid. Start again from the first page.";

    private static readonly string[] _invalidCursor = [InvalidCursorMessage];

    public async Task<OrderListPage> ListAsync(OrderListFilter filter, string? cursor, int limit, CancellationToken cancellationToken)
    {
        OrderCursor? after = null;
        if (cursor is not null && !OrderCursor.TryDecode(cursor, out after))
        {
            throw new DomainValidationException(new Dictionary<string, string[]> { ["cursor"] = _invalidCursor });
        }

        var orders = db.Orders.AsNoTracking();

        // Each filter narrows the same index-backed scan: (tenant, status, created_at, number) when a
        // status is chosen (the packer's usual view), otherwise (tenant, created_at, number).
        if (filter.Statuses is { Count: > 0 } statuses)
        {
            orders = orders.Where(o => statuses.Contains(o.Status));
        }

        if (filter.CustomerId is { } customerId)
        {
            // One buyer's orders: (tenant_id, customer_id, created_at, number) reads them newest first, no sort.
            orders = orders.Where(o => o.CustomerId == customerId);
        }

        if (filter.From is { } from)
        {
            var start = DhakaTime.StartOfDay(from);
            orders = orders.Where(o => o.CreatedAt >= start);
        }

        if (filter.To is { } to)
        {
            var end = DhakaTime.StartOfDay(to.AddDays(1)); // inclusive Dhaka day = before the next local midnight
            orders = orders.Where(o => o.CreatedAt < end);
        }

        if (filter.IsInsideDhaka is { } inside)
        {
            orders = orders.Where(o => o.IsInsideDhaka == inside);
        }

        if (filter.Source is { } source)
        {
            orders = orders.Where(o => o.Source == source);
        }

        if (after is { } bookmark)
        {
            // Row-value comparison: (created_at, number) < (@at, @number). Seeks the index to the
            // bookmark instead of reading and discarding every earlier row like OFFSET does.
            orders = orders.Where(o => EF.Functions.LessThan(
                ValueTuple.Create(o.CreatedAt, o.Number),
                ValueTuple.Create(bookmark.CreatedAt, bookmark.Number)));
        }

        var rows = orders.Join(
            db.Customers.AsNoTracking(),
            o => o.CustomerId,
            c => c.Id,
            (o, c) => new { Order = o, c.Phone, c.Name });

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var text = filter.Search.Trim().TrimStart('#');
            var digits = new string([.. text.Where(char.IsAsciiDigit)]);
            var namePattern = $"%{EscapeLike(text)}%";
            var phonePattern = $"%{digits}%";
            int? number = digits.Length == text.Length && int.TryParse(digits, out var n) ? n : null;
            var matchPhone = digits.Length >= 3; // short digit runs would match everyone

            // "#1043" or "1043" finds order 1043; three or more digits also match phone fragments
            // ("01712" finds +8801712...); any text matches the customer name.
            rows = rows.Where(r =>
                (number != null && r.Order.Number == number)
                || (matchPhone && EF.Functions.Like(r.Phone, phonePattern))
                || EF.Functions.ILike(r.Name!, namePattern, @"\"));
        }

        var page = await rows
            // Number breaks ties in created_at: unique per shop and given out in creation order,
            // so "newest first" stays exact and the cursor always names one row.
            .OrderByDescending(r => r.Order.CreatedAt).ThenByDescending(r => r.Order.Number)
            .Take(limit + 1) // one extra row says whether a next page exists, without a COUNT
            .ToListAsync(cancellationToken);

        var items = page.Take(limit).Select(r => new OrderListItem(
            r.Order.Id,
            r.Order.Number,
            r.Order.Status,
            r.Order.Source,
            r.Order.IsInsideDhaka,
            r.Order.CustomerId,
            r.Phone,
            r.Name,
            r.Order.Lines.Sum(l => l.Quantity),
            r.Order.Total.Amount,
            r.Order.CodAmount.Amount,
            r.Order.CreatedAt)).ToList();

        var next = page.Count > limit ? new OrderCursor(items[^1].CreatedAt, items[^1].Number).Encode() : null;
        return new OrderListPage(items, next);
    }

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
