using Hishab.Application.Couriers;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Hishab.Infrastructure.Couriers;

/// <summary>
/// Location lists in memory for a day, per (courier, level, parent): the same for every shop, so
/// one shop's fetch serves all of them, and a new list reaches us within a day of the courier
/// adding a zone. In-process on purpose: a second API instance costs one extra fetch a day, not
/// a shared cache. Empty lists are not cached, so a made-up parent ID cannot fill memory, and a
/// zone list is only fetched for a city the courier itself listed.
/// </summary>
internal sealed partial class CourierLocationService(
    ICourierProviderRegistry registry,
    ICourierAccountService accounts,
    IMemoryCache cache,
    ILogger<CourierLocationService> logger) : ICourierLocationService
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);

    public async Task<IReadOnlyList<CourierLocation>> ListAsync(
        CourierProvider provider, LocationLevel level, string? parentId, CancellationToken cancellationToken)
    {
        var adapter = registry.Find(provider);
        if (adapter is not { SupportsLocations: true })
        {
            throw new DomainValidationException("provider", $"{provider} does not use delivery locations; it delivers by address.");
        }

        parentId = string.IsNullOrWhiteSpace(parentId) ? null : parentId.Trim();
        if (level == LocationLevel.City ? parentId is not null : parentId is null)
        {
            throw new DomainValidationException("parentId", level == LocationLevel.City
                ? "Cities have no parent location."
                : $"Choose the {(level == LocationLevel.Zone ? "city" : "zone")} first.");
        }

        if (level == LocationLevel.Zone)
        {
            await EnsureListedAsync(provider, LocationLevel.City, null, parentId!, "parentId", "This city is not in the courier's list.", cancellationToken);
        }

        var key = $"courier-locations:{provider}:{level}:{parentId}";
        if (cache.TryGetValue(key, out IReadOnlyList<CourierLocation>? cached) && cached is not null)
        {
            return cached;
        }

        var keys = await CredentialsAsync(provider, cancellationToken);
        IReadOnlyList<CourierLocation> list =
            [.. (await adapter.GetLocationsAsync(keys, level, parentId, cancellationToken)).OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)];
        if (list.Count > 0)
        {
            cache.Set(key, list, CacheFor);
        }

        LogFetched(logger, provider, level, list.Count);
        return list;
    }

    public async Task EnsureValidAsync(CourierProvider provider, CourierDestination destination, CancellationToken cancellationToken)
    {
        // Listing the city's zones also checks the city (an unknown one is reported on cityId).
        await EnsureListedAsync(provider, LocationLevel.Zone, destination.CityId, destination.ZoneId, "destination.zoneId", "This zone is not in the chosen city.", cancellationToken, parentField: "destination.cityId");
        if (destination.AreaId is not null)
        {
            await EnsureListedAsync(provider, LocationLevel.Area, destination.ZoneId, destination.AreaId, "destination.areaId", "This area is not in the chosen zone.", cancellationToken);
        }
    }

    private async Task EnsureListedAsync(
        CourierProvider provider,
        LocationLevel level,
        string? parentId,
        string id,
        string field,
        string message,
        CancellationToken cancellationToken,
        string parentField = "parentId")
    {
        IReadOnlyList<CourierLocation> list;
        try
        {
            list = await ListAsync(provider, level, parentId, cancellationToken);
        }
        catch (DomainValidationException e) when (e.Errors.TryGetValue("parentId", out var reasons))
        {
            throw new DomainValidationException(parentField, reasons[0]); // keyed to the caller's field
        }

        if (list.All(l => l.Id != id))
        {
            throw new DomainValidationException(field, message);
        }
    }

    private async Task<CourierCredentials> CredentialsAsync(CourierProvider provider, CancellationToken cancellationToken)
    {
        try
        {
            return await accounts.GetCredentialsAsync(provider, cancellationToken);
        }
        catch (NotFoundException)
        {
            throw new DomainValidationException("provider", $"Connect {provider} in courier settings first.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Fetched {Count} {Level} locations from {Provider} (cached for a day)")]
    private static partial void LogFetched(ILogger logger, CourierProvider provider, LocationLevel level, int count);
}
