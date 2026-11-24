using Hishab.Application.Orders;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Orders;
using Hishab.Domain.Shops;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Hishab.Infrastructure.Couriers;

/// <summary>
/// The reverse leg: a returned parcel arriving back at the shop, and what the courier charged to
/// carry it back. The charge is a cost, so entering or reading it needs SeeProfit; receiving the
/// box does not (packers do it).
/// </summary>
internal sealed partial class ShipmentService
{
    public const string ChargeNeedsProfitMessage = "Only staff who can see profit can enter the return charge.";

    public async Task<OrderDetails> ReceiveBackAsync(Guid orderId, Money? returnCharge, string? note, CancellationToken cancellationToken)
    {
        if (returnCharge is not null)
        {
            EnsureCanSeeProfit();
        }

        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);
        order.TransitionTo(OrderStatus.ReceivedBack, request.UserId, clock.GetUtcNow(), note); // 409 unless Returned

        if (returnCharge is { } charge)
        {
            (await ReturnedShipmentAsync(orderId, cancellationToken)).RecordReturnCharge(charge);
        }

        await SaveReturnAsync(cancellationToken); // status and charge together, or neither
        LogReceivedBack(orderId, returnCharge is not null);
        return await orders.GetAsync(orderId, cancellationToken);
    }

    public async Task<OrderDetails> SetReturnChargeAsync(Guid orderId, Money returnCharge, CancellationToken cancellationToken)
    {
        EnsureCanSeeProfit();

        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new NotFoundException("Order", orderId);
        if (order.Status is not (OrderStatus.Returned or OrderStatus.ReceivedBack))
        {
            throw new ConflictException($"Only a returned parcel has a return charge; this order is {order.Status}.");
        }

        (await ReturnedShipmentAsync(orderId, cancellationToken)).RecordReturnCharge(returnCharge);
        await SaveReturnAsync(cancellationToken);
        return await orders.GetAsync(orderId, cancellationToken);
    }

    private void EnsureCanSeeProfit()
    {
        if (!request.Has(Permission.SeeProfit))
        {
            throw new ForbiddenException(ChargeNeedsProfitMessage);
        }
    }

    /// <summary>The parcel that went out and came back: the order's one booked shipment (one live booking per order).</summary>
    private async Task<Shipment> ReturnedShipmentAsync(Guid orderId, CancellationToken cancellationToken) =>
        await db.Shipments.SingleOrDefaultAsync(s => s.OrderId == orderId && s.State == ShipmentState.Booked, cancellationToken)
        ?? throw new ConflictException("This order has no booked parcel to charge a return for.");

    private async Task SaveReturnAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Someone else changed this order at the same time. Reload it and try again.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Order {OrderId} received back (return charge entered: {WithCharge})")]
    private partial void LogReceivedBack(Guid orderId, bool withCharge);
}
