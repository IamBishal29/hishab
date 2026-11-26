using Hishab.Application.Orders;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.Infrastructure.Couriers.Fake;

/// <summary>
/// Demo and e2e helper: plays the Fake courier moving one of this shop's parcels, then runs the same status
/// sync the background job runs, so the order moves exactly as it would for a real courier. Only reachable
/// through the dev-only endpoint (Development and Demo).
/// </summary>
public sealed class FakeCourierSimulator(
    HishabDbContext db, FakeCourierControl control, IServiceProvider services, IOrderService orders, TimeProvider clock)
{
    public async Task<OrderDetails> MoveAsync(Guid orderId, ParcelStatus status, CancellationToken cancellationToken)
    {
        // Tenant filter on: another shop's order is simply not found.
        var shipment = await db.Shipments
            .Where(s => s.OrderId == orderId)
            .OrderByDescending(s => s.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Shipment", orderId);

        if (shipment.Provider != CourierProvider.Fake || shipment.ConsignmentId is null)
        {
            throw new ConflictException("Only parcels booked with the Fake courier can be moved here.");
        }

        control.SetStatus(shipment.ConsignmentId, status, clock.GetUtcNow());
        await services.GetRequiredService<CourierStatusSync>().SyncAsync(CourierProvider.Fake, cancellationToken);
        return await orders.GetAsync(orderId, cancellationToken);
    }
}
