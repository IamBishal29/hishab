using Hishab.Application.Orders;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hishab.Infrastructure.Labels;

/// <summary>
/// Labels for booked orders of the current shop. All reads go through the tenant filter, so an ID
/// from another shop is simply "not found". Three queries for any number of orders (orders,
/// their booked shipments, their customers), never one per order.
/// </summary>
internal sealed partial class LabelService(HishabDbContext db, ChromiumPdfRenderer renderer, ILogger<LabelService> logger) : ILabelService
{
    public async Task<LabelDocument> RenderAsync(IReadOnlyList<Guid> orderIds, LabelFormat format, CancellationToken cancellationToken)
    {
        var ids = orderIds.Distinct().ToList();
        var orders = await db.Orders.AsNoTracking().Where(o => ids.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var missing = ids.Where(id => !orders.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException("Order", missing[0]);
        }

        var shipments = await db.Shipments.AsNoTracking()
            .Where(s => ids.Contains(s.OrderId) && s.State == ShipmentState.Booked)
            .ToDictionaryAsync(s => s.OrderId, cancellationToken); // at most one live shipment per order (unique index)
        var notBooked = ids.Where(id => !shipments.ContainsKey(id)).Select(id => orders[id].Number).Order().ToList();
        if (notBooked.Count > 0)
        {
            throw new DomainValidationException("orderIds", $"Book these orders with a courier first: #{string.Join(", #", notBooked)}.");
        }

        var customerIds = orders.Values.Select(o => o.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        var shop = await db.Shops.AsNoTracking().SingleAsync(cancellationToken);

        var labels = ids.Select(id =>
        {
            var order = orders[id];
            var shipment = shipments[id];
            var customer = customers[order.CustomerId];
            return new ShippingLabel(
                shop.Name,
                shop.PickupAddress,
                order.Number,
                customer.Name ?? "Customer",
                LocalPhone(customer.Phone),
                order.DeliveryAddress ?? customer.Address ?? string.Empty,
                order.IsInsideDhaka,
                order.CodAmount.Amount,
                order.Lines.Sum(l => l.Quantity),
                shipment.Provider.ToString(),
                shipment.ConsignmentId!,
                shipment.TrackingCode,
                order.Note);
        }).ToList();

        var pdf = await renderer.RenderAsync(LabelHtml.Build(labels, format), cancellationToken);
        LogRendered(logger, labels.Count, format);
        return new LabelDocument(pdf, labels.Count);
    }

    /// <summary>Riders dial the local form (01XXXXXXXXX); we store E.164.</summary>
    private static string LocalPhone(string e164) => e164.StartsWith("+880", StringComparison.Ordinal) ? "0" + e164[4..] : e164;

    [LoggerMessage(Level = LogLevel.Information, Message = "Printed {Count} shipping labels ({Format})")]
    private static partial void LogRendered(ILogger logger, int count, LabelFormat format);
}
