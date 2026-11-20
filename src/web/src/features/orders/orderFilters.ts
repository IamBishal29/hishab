import { OrderSource, OrderStatus, type GetApiOrdersParams } from '@/api/generated/hishab'

/**
 * The order list's filters live in the URL (shareable, survive reload and Back). These two
 * functions are the only translation between the URL and the API's query parameters.
 * Unknown or malformed values (a hand-edited link) are dropped instead of sent to the API.
 * The paging cursor is deliberately not here: it is a position in one result, not a filter.
 */
export interface OrderFilters {
  status: OrderStatus[]
  from: string | null
  to: string | null
  area: 'inside' | 'outside' | null
  source: OrderSource | null
  q: string
}

const isoDay = /^\d{4}-\d{2}-\d{2}$/
const statuses = new Set<string>(Object.values(OrderStatus))
const sources = new Set<string>(Object.values(OrderSource))

export function parseFilters(params: URLSearchParams): OrderFilters {
  const day = (key: string) => {
    const value = params.get(key)
    return value && isoDay.test(value) ? value : null
  }
  const area = params.get('area')
  const source = params.get('source')
  return {
    status: [...new Set(params.getAll('status'))].filter((s): s is OrderStatus => statuses.has(s)),
    from: day('from'),
    to: day('to'),
    area: area === 'inside' || area === 'outside' ? area : null,
    source: source && sources.has(source) ? (source as OrderSource) : null,
    q: params.get('q')?.trim() ?? '',
  }
}

/** Only non-default values, in a fixed order, so equal filters give equal URLs. */
export function toSearchParams(filters: OrderFilters): URLSearchParams {
  const params = new URLSearchParams()
  for (const s of filters.status) params.append('status', s)
  if (filters.from) params.set('from', filters.from)
  if (filters.to) params.set('to', filters.to)
  if (filters.area) params.set('area', filters.area)
  if (filters.source) params.set('source', filters.source)
  if (filters.q) params.set('q', filters.q)
  return params
}

export function toApiParams(filters: OrderFilters): GetApiOrdersParams {
  return {
    ...(filters.status.length > 0 && { Status: filters.status }),
    ...(filters.from && { From: filters.from }),
    ...(filters.to && { To: filters.to }),
    ...(filters.area && { IsInsideDhaka: filters.area === 'inside' }),
    ...(filters.source && { Source: filters.source }),
    ...(filters.q && { Search: filters.q }),
  }
}
