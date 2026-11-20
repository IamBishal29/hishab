import { infiniteQueryOptions, type QueryClient } from '@tanstack/react-query'
import {
  getApiOrders,
  getGetApiOrdersIdQueryKey,
  getGetApiOrdersQueryKey,
  OrderStatus,
  type GetApiOrdersParams,
} from '@/api/generated/hishab'

export const pageSize = 25

/**
 * Keyset paging: each page carries the cursor for the next one, so "Load more" never skips or
 * repeats an order when new orders arrive at the top (OFFSET paging would). The key starts with
 * the generated list key, so one prefix invalidation reaches every filtered list.
 */
export const orderListQuery = (params: GetApiOrdersParams) =>
  infiniteQueryOptions({
    queryKey: [...getGetApiOrdersQueryKey(params), 'infinite'],
    queryFn: ({ pageParam, signal }) =>
      getApiOrders({ ...params, Limit: pageSize, Cursor: pageParam }, { signal }),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })

/**
 * After a status change the cached lists (any filter) and the changed orders' details are stale.
 * Mark them stale and refetch what is on screen, rather than patching cached pages by hand: the
 * server decides which filters an order now matches.
 */
export function invalidateOrders(queryClient: QueryClient, orderIds: readonly string[]): void {
  void queryClient.invalidateQueries({ queryKey: getGetApiOrdersQueryKey() })
  for (const id of orderIds) {
    void queryClient.invalidateQueries({ queryKey: getGetApiOrdersIdQueryKey(id) })
  }
}

/** The PRD lifecycle steps a person can take from the order screens (courier steps come later). */
export const canConfirm = (status: OrderStatus): boolean => status === OrderStatus.New
export const canCancel = (status: OrderStatus): boolean =>
  status === OrderStatus.New || status === OrderStatus.Confirmed
