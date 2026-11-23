import { keepPreviousData, useQuery } from '@tanstack/react-query'
import {
  getGetApiCouriersProviderLocationsQueryOptions,
  LocationLevel,
  postApiOrdersIdQuote,
  type CourierDestination,
  type CourierLocation,
  type CourierProvider,
} from '@/api/generated/hishab'

/** The API caches courier lists for 24 hours; an hour in the browser is plenty for one session. */
const LOCATION_STALE_MS = 60 * 60 * 1000

/**
 * One level of a courier's location list. The key holds the provider, level and parent, so each
 * city's zones are their own cache entry: going back to a city shows its zones at once, and a
 * re-render never refetches. A level waits for its parent (`parentId` null = not chosen yet).
 */
export function useLocations(
  provider: CourierProvider | null,
  level: LocationLevel,
  parentId: string | null,
): { data: CourierLocation[]; isLoading: boolean; isError: boolean } {
  const needsParent = level !== LocationLevel.City
  const params = needsParent ? { level, parentId: parentId ?? '' } : { level }
  const query = useQuery({
    ...getGetApiCouriersProviderLocationsQueryOptions(provider ?? 'Fake', params),
    enabled: provider !== null && (!needsParent || parentId !== null),
    staleTime: LOCATION_STALE_MS,
  })
  return { data: query.data ?? [], isLoading: query.isLoading, isError: query.isError }
}

/** City and zone chosen (area optional): a destination the API accepts. */
export function isComplete(
  destination: Partial<CourierDestination>,
): destination is CourierDestination {
  return Boolean(destination.cityId && destination.zoneId)
}

/**
 * The courier's price for this order to a complete destination. A query, not a mutation: the
 * endpoint is a POST only because it takes a body, and saves nothing. Keyed by every input, so
 * changing the zone asks again while the previous price stays visible until the new one lands.
 */
export function useQuote(
  orderId: string,
  courier: CourierProvider | null,
  destination: CourierDestination | null,
) {
  return useQuery({
    queryKey: ['orderQuote', orderId, courier, destination] as const,
    queryFn: ({ signal }) => postApiOrdersIdQuote(orderId, { courier, destination }, { signal }),
    enabled: courier !== null && destination !== null,
    placeholderData: keepPreviousData,
    staleTime: 60_000,
    retry: false,
  })
}
