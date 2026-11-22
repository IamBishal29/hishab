using Hishab.Application.Orders;
using Hishab.Domain.Common;
using Hishab.Domain.Orders;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hishab.Infrastructure.Labels;

/// <summary>
/// Invoices for the current shop's orders. All reads go through the tenant filter (an ID from
/// another shop is "not found"); three queries for any number of orders. The order is projected
/// into <see cref="Invoice"/> here, field by field: costs stay behind, whatever the caller may see.
/// </summary>
internal sealed partial class InvoiceService(HishabDbContext db, ChromiumPdfRenderer renderer, ILogger<InvoiceService> logger) : IInvoiceService
{
    public async Task<InvoiceDocument> RenderAsync(IReadOnlyList<Guid> orderIds, InvoiceFormat format, CancellationToken cancellationToken)
    {
        var ids = orderIds.Distinct().ToList();
        var orders = await db.Orders.AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var missing = ids.Where(id => !orders.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException("Order", missing[0]);
        }

        var cancelled = ids.Select(id => orders[id]).Where(o => o.Status == OrderStatus.Cancelled).Select(o => o.Number).Order().ToList();
        if (cancelled.Count > 0)
        {
            throw new DomainValidationException("orderIds", $"Cancelled orders have no invoice: #{string.Join(", #", cancelled)}.");
        }

        var customerIds = orders.Values.Select(o => o.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var shop = await db.Shops.AsNoTracking().SingleAsync(cancellationToken);

        var invoices = ids.Select(id =>
        {
            var order = orders[id];
            var customer = customers[order.CustomerId];
            return new Invoice(
                shop.Name,
                shop.PickupAddress,
                order.Number,
                DhakaTime.DayOf(order.CreatedAt), // the seller's calendar day, not UTC
                customer.Name ?? "Customer",
                PrintHtml.LocalPhone(customer.Phone),
                order.DeliveryAddress ?? customer.Address,
                [.. order.Lines.Select(l => new InvoiceLine(l.ProductName, l.VariantLabel, l.Quantity, l.UnitPrice.Amount, l.LineTotal.Amount))],
                order.Subtotal.Amount,
                order.DeliveryCharge.Amount,
                order.Total.Amount,
                order.AdvancePaid.Amount,
                order.CodAmount.Amount,
                order.Note);
        }).ToList();

        var pdf = await renderer.RenderAsync(InvoiceHtml.Build(invoices, format), cancellationToken);
        LogRendered(logger, invoices.Count, format);
        return new InvoiceDocument(pdf, invoices.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Printed {Count} invoices ({Format})")]
    private static partial void LogRendered(ILogger logger, int count, InvoiceFormat format);
}
