import { useQuery } from '@tanstack/react-query'
import { postApiCustomersLookup, type CustomerLookupResponse } from '@/api/generated/hishab'
import { ApiProblem } from '@/api/http'
import { normalizePhone } from '@/lib/phone'
import { useDebouncedValue } from '@/lib/useDebouncedValue'

/**
 * Server state for the phone being typed: the known customer and their history, or null for a
 * new customer. Waits for a pause in typing and for a complete number; one cache entry per
 * normalised number, so going back to an earlier number is instant.
 */
export function useCustomerLookup(phoneInput: string) {
  const phone = normalizePhone(useDebouncedValue(phoneInput))
  const query = useQuery({
    queryKey: ['customers', 'lookup', phone],
    queryFn: async (): Promise<CustomerLookupResponse | null> => {
      try {
        return await postApiCustomersLookup({ phone: phone ?? '' })
      } catch (error) {
        if (error instanceof ApiProblem && error.status === 404) return null // a new customer
        throw error
      }
    },
    enabled: phone !== null,
  })
  return { phone, ...query }
}
