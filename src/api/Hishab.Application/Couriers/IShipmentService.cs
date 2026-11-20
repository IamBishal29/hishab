using Hishab.Application.Orders;
using Hishab.Domain.Couriers;

namespace Hishab.Application.Couriers;

/// <summary>Books orders with couriers, keeping our database and the courier's consistent.</summary>
public interface IShipmentService
{
    /// <summary>
    /// Books a confirmed order with <paramref name="courier"/> (or the shop's default courier) and
    /// moves it to Booked. Safe to retry: an already booked order returns its booking, and an
    /// unfinished attempt is resent with the same reference, so the courier never books it twice.
    /// </summary>
    /// <exception cref="Domain.Common.NotFoundException">No such order in this shop.</exception>
    /// <exception cref="Domain.Common.ConflictException">The order is not confirmed, or another booking is in progress.</exception>
    /// <exception cref="Domain.Common.DomainValidationException">No courier chosen or connected, no address, or the courier refused the parcel.</exception>
    /// <exception cref="CourierUnavailableException">The courier did not answer; retrying is safe.</exception>
    Task<OrderDetails> BookAsync(Guid orderId, CourierProvider? courier, CancellationToken cancellationToken);
}
