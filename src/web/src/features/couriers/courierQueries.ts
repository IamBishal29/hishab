import type { QueryClient } from '@tanstack/react-query'
import {
  getGetApiCouriersQueryKey,
  getGetApiShopQueryKey,
  useGetApiCouriers,
  useGetApiShop,
  type CourierConnection,
  type CourierProvider,
} from '@/api/generated/hishab'

/**
 * Writes a courier's new state (after connect, replace or disconnect) into the cached list, then
 * refetches the shop: disconnecting the default courier clears it on the server. The list holds
 * only what GET returns (connected, masked hint), never the keys that were typed.
 */
export function afterCourierChange(queryClient: QueryClient, updated: CourierConnection): void {
  queryClient.setQueryData<CourierConnection[]>(getGetApiCouriersQueryKey(), (list) =>
    list?.map((c) => (c.provider === updated.provider ? updated : c)),
  )
  void queryClient.invalidateQueries({ queryKey: getGetApiCouriersQueryKey() })
  void queryClient.invalidateQueries({ queryKey: getGetApiShopQueryKey() })
}

export interface BookingChoice {
  /** Couriers the shop can book with right now. */
  connected: CourierProvider[]
  /** Preselected: the shop's default courier if connected, else the only connected one. */
  preferred: CourierProvider | null
  isPending: boolean
}

/** What the booking screens offer: every staff member may read both endpoints. */
export function useBookingChoice(): BookingChoice {
  const couriers = useGetApiCouriers()
  const shop = useGetApiShop()
  const connected = (couriers.data ?? []).filter((c) => c.isConnected).map((c) => c.provider)
  const fallback = connected.length === 1 ? (connected[0] ?? null) : null
  const defaultCourier = shop.data?.defaultCourier ?? null
  return {
    connected,
    preferred: defaultCourier && connected.includes(defaultCourier) ? defaultCourier : fallback,
    isPending: couriers.isPending || shop.isPending,
  }
}
