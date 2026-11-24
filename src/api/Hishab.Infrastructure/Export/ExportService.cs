using System.Globalization;
using Hishab.Application.Export;
using Hishab.Application.Orders;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Orders;
using Hishab.Infrastructure.Customers;
using Hishab.Infrastructure.Labels;
using Hishab.Infrastructure.Orders;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hishab.Infrastructure.Export;

/// <summary>
/// Streams CSV: each query is read with AsAsyncEnumerable and every row is written as it arrives,
/// so memory stays flat whether the shop has a hundred orders or a million. The filters are the
/// list screens' own (OrderService.Filter, CustomerService.Search), and the tenant filter applies
/// to every query as usual.
/// </summary>
internal sealed class ExportService(HishabDbContext db) : IExportService
{
    private static readonly OrderStatus[] _delivered = [OrderStatus.Delivered, OrderStatus.CodSettled];
    private static readonly OrderStatus[] _returned = [OrderStatus.Returned, OrderStatus.ReceivedBack];

    public async Task WriteOrdersAsync(OrderListFilter filter, Stream output, CancellationToken cancellationToken)
    {
        await using var text = new StreamWriter(output, CsvWriter.Encoding, bufferSize: 16 * 1024, leaveOpen: true);
        var csv = new CsvWriter(text);
        await csv.HeaderAsync(
            "number", "created_at", "status", "source", "customer_name", "customer_phone", "delivery_address", "area",
            "items", "subtotal", "delivery_charge", "total", "advance_paid", "cod_amount", "product_cost", "courier",
            "consignment_id", "note");

        var rows = OrderService.Filter(db, filter)
            .OrderByDescending(r => r.Order.CreatedAt).ThenByDescending(r => r.Order.Number)
            .Select(r => new
            {
                r.Order.Number,
                r.Order.CreatedAt,
                r.Order.Status,
                r.Order.Source,
                r.Name,
                r.Phone,
                r.Order.DeliveryAddress,
                r.Order.IsInsideDhaka,
                r.Order.DeliveryCharge,
                r.Order.AdvancePaid,
                r.Order.Note,
                Lines = r.Order.Lines.Select(l => new { l.ProductName, l.VariantLabel, l.Quantity, l.UnitPrice, l.UnitCost }).ToList(),
                Parcel = db.Shipments
                    .Where(s => s.OrderId == r.Order.Id && s.State == ShipmentState.Booked)
                    .Select(s => new { s.Provider, s.ConsignmentId })
                    .FirstOrDefault(),
            })
            .AsAsyncEnumerable();

        await foreach (var o in rows.WithCancellation(cancellationToken))
        {
            var subtotal = o.Lines.Sum(l => l.UnitPrice.Amount * l.Quantity);
            var total = subtotal + o.DeliveryCharge.Amount;
            // Blank rather than a partial sum when any line has no cost price: a low number would look real.
            decimal? cost = o.Lines.All(l => l.UnitCost is not null) ? o.Lines.Sum(l => l.UnitCost!.Value.Amount * l.Quantity) : null;
            var items = string.Join("; ", o.Lines.Select(l => $"{l.ProductName}{(l.VariantLabel is null ? "" : $" ({l.VariantLabel})")} x{l.Quantity}"));

            await csv.Number(o.Number).Raw(DhakaDateTime(o.CreatedAt)).Raw(o.Status.ToString()).Raw(o.Source.ToString())
                .Text(o.Name).Raw(Phone(o.Phone)).Text(o.DeliveryAddress).Raw(o.IsInsideDhaka ? "Inside Dhaka" : "Outside Dhaka")
                .Text(items).Number(subtotal).Number(o.DeliveryCharge.Amount).Number(total).Number(o.AdvancePaid.Amount)
                .Number(total - o.AdvancePaid.Amount).Number(cost).Raw(o.Parcel?.Provider.ToString()).Text(o.Parcel?.ConsignmentId)
                .Text(o.Note)
                .EndRowAsync();
        }
    }

    public async Task WriteCustomersAsync(string? search, Stream output, CancellationToken cancellationToken)
    {
        await using var text = new StreamWriter(output, CsvWriter.Encoding, bufferSize: 16 * 1024, leaveOpen: true);
        var csv = new CsvWriter(text);
        await csv.HeaderAsync(
            "name", "phone", "address", "notes", "blocked", "blocked_reason", "created_at", "orders", "delivered", "returned", "cancelled");

        // Counts are correlated subqueries on the (tenant_id, customer_id, ...) index: one statement, streamed.
        var rows = CustomerService.Search(db.Customers.AsNoTracking(), search)
            .OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            .Select(c => new
            {
                c.Name,
                c.Phone,
                c.Address,
                c.Notes,
                c.IsBlocked,
                c.BlockedReason,
                c.CreatedAt,
                Orders = db.Orders.Count(o => o.CustomerId == c.Id),
                Delivered = db.Orders.Count(o => o.CustomerId == c.Id && _delivered.Contains(o.Status)),
                Returned = db.Orders.Count(o => o.CustomerId == c.Id && _returned.Contains(o.Status)),
                Cancelled = db.Orders.Count(o => o.CustomerId == c.Id && o.Status == OrderStatus.Cancelled),
            })
            .AsAsyncEnumerable();

        await foreach (var c in rows.WithCancellation(cancellationToken))
        {
            await csv.Text(c.Name).Raw(Phone(c.Phone)).Text(c.Address).Text(c.Notes).Raw(c.IsBlocked ? "yes" : "no")
                .Text(c.BlockedReason).Raw(DhakaDateTime(c.CreatedAt))
                .Number(c.Orders).Number(c.Delivered).Number(c.Returned).Number(c.Cancelled)
                .EndRowAsync();
        }
    }

    /// <summary>Local time, as the seller thinks of it; spreadsheets read this as a date and time.</summary>
    private static string DhakaDateTime(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, DhakaTime.Zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// "01912-345678": the way Bangladeshi numbers are written, and the hyphen keeps it text in Excel
    /// (a bare 01912345678 would lose its leading zero; +880... would become 8.8E+12).
    /// </summary>
    internal static string Phone(string e164)
    {
        var local = PrintHtml.LocalPhone(e164);
        return local.Length == 11 && local.StartsWith('0') ? $"{local[..5]}-{local[5..]}" : local;
    }
}
