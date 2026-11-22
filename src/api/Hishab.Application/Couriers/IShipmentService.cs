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
    /// <remarks>
    /// <paramref name="destination"/> (couriers with location lists only) is checked against the
    /// courier's lists and saved on the shipment; null keeps what an earlier attempt saved, or books
    /// by address. An unfinished attempt cannot change it (409).
    /// </remarks>
    /// <exception cref="CourierUnavailableException">The courier did not answer; retrying is safe.</exception>
    Task<OrderDetails> BookAsync(Guid orderId, CourierProvider? courier, CourierDestination? destination, CancellationToken cancellationToken);

    /// <summary>The courier's price for this order (to the destination, for couriers that need one). Saves nothing.</summary>
    /// <exception cref="Domain.Common.NotFoundException">No such order in this shop.</exception>
    /// <exception cref="Domain.Common.DomainValidationException">No courier, an invalid destination, or the courier needs one.</exception>
    /// <exception cref="CourierUnavailableException">The courier did not answer.</exception>
    Task<CourierQuote> QuoteAsync(Guid orderId, CourierProvider? courier, CourierDestination? destination, CancellationToken cancellationToken);

    /// <summary>
    /// Books many orders with one courier. Each order succeeds or fails on its own: booked orders
    /// stay booked when others fail, and every failure is safe to retry (same references).
    /// </summary>
    /// <exception cref="Domain.Common.DomainValidationException">No courier chosen or connected (nothing is booked).</exception>
    Task<BulkActionResult> BookManyAsync(IReadOnlyList<Guid> orderIds, CourierProvider? courier, CancellationToken cancellationToken);
}
