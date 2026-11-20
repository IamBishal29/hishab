import type { QueryClient } from '@tanstack/react-query'
import {
  getGetApiCustomersIdQueryKey,
  getGetApiOrdersQueryKey,
  type CustomerDetails,
  type CustomerDto,
} from '@/api/generated/hishab'

/**
 * Every screen that shows a customer, in one place. A change made here (name, notes, block)
 * must reach all of them, or the quick order form keeps showing a blocked buyer as fine.
 * - this customer's page: write the server's answer straight in (it is the new truth);
 * - order lists and order details: they show the name and the blocked flag, so refetch;
 * - the quick order phone lookup: its blocked warning must appear on the next order.
 */
export function afterCustomerChange(queryClient: QueryClient, updated: CustomerDto): void {
  queryClient.setQueryData<CustomerDetails>(getGetApiCustomersIdQueryKey(updated.id), (old) =>
    old ? { ...old, customer: updated } : old,
  )
  void queryClient.invalidateQueries({ queryKey: getGetApiOrdersQueryKey() })
  void queryClient.invalidateQueries({
    predicate: (query) =>
      typeof query.queryKey[0] === 'string' && query.queryKey[0].startsWith('/api/orders/'),
  })
  void queryClient.invalidateQueries({ queryKey: ['customers', 'lookup'] })
}

/** Delivered out of the orders that reached an end at the door (delivered or returned). */
export function deliveryRate(delivered: number, returned: number): number | null {
  const finished = delivered + returned
  return finished === 0 ? null : Math.round((delivered / finished) * 100)
}
