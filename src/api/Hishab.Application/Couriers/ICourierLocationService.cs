using Hishab.Domain.Couriers;

namespace Hishab.Application.Couriers;

/// <summary>
/// A courier's location lists (city → zone → area), cached: they are the same for every shop and
/// change a few times a year, so the courier is asked at most once a day per list, not once per
/// booking form. Fetched with the current shop's own keys (the courier needs a login to answer).
/// </summary>
public interface ICourierLocationService
{
    /// <exception cref="Domain.Common.DomainValidationException">The courier has no location lists, is not connected, or the level/parent does not fit.</exception>
    /// <exception cref="CourierUnavailableException">The courier did not answer (nothing is cached).</exception>
    Task<IReadOnlyList<CourierLocation>> ListAsync(
        CourierProvider provider, LocationLevel level, string? parentId, CancellationToken cancellationToken);

    /// <summary>
    /// Checks that the zone belongs to the city and the area to the zone, so a typo or a stale form
    /// gets a clear 400 here instead of a confusing refusal from the courier.
    /// </summary>
    /// <exception cref="Domain.Common.DomainValidationException">An ID is not in its parent's list.</exception>
    Task EnsureValidAsync(CourierProvider provider, CourierDestination destination, CancellationToken cancellationToken);
}
