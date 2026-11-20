import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type {
  BulkItemResult,
  OrderDetails,
  OrderListItem,
  OrderStatus,
} from '@/api/generated/hishab'

/** A fake Hishab API for component tests. `api` holds the state tests arrange and inspect. */
export const api = {
  /** Whether the browser holds a valid refresh cookie. */
  hasRefreshCookie: false,
  isNewShop: true,
  refreshCalls: 0,
  shopName: 'My shop',
  /** Known customers by local 11-digit phone, for POST /api/customers/lookup. */
  customers: {} as Record<string, LookupResult>,
  /** Every POST /api/orders as received: idempotency key and body. */
  orderRequests: [] as { key: string | null; body: Record<string, unknown> }[],
  /** The next POST /api/orders fails like this instead of succeeding. */
  nextOrderFailure: null as null | 'network' | Record<string, string[]>,
  /** The signed-in user's role (GET /api/auth/me). */
  role: 'Owner',
  /** Orders served by GET /api/orders (newest first) and changed by confirm/cancel. */
  orders: [] as OrderListItem[],
  /** Query strings of every GET /api/orders, to check what the list asked for. */
  orderListQueries: [] as URLSearchParams[],
  /** Bulk or single actions on these order IDs fail with this message (lifecycle conflict). */
  conflicts: {} as Record<string, string>,
}

export function listItem(number: number, overrides: Partial<OrderListItem> = {}): OrderListItem {
  return {
    id: `o-${String(number)}`,
    number,
    status: 'New',
    source: 'Facebook',
    isInsideDhaka: true,
    customerId: 'c-1',
    customerPhone: '+8801712345678',
    customerName: 'Rahima Begum',
    itemCount: 2,
    total: 1360,
    codAmount: 1360,
    createdAt: '2026-11-20T04:30:00Z',
    ...overrides,
  }
}

/** GET /api/orders/{id} for a list item: one line, created then (maybe) moved on. */
export function detailsOf(item: OrderListItem): OrderDetails {
  const history: OrderDetails['order']['history'] = [
    { from: null, to: 'New', changedBy: 'u-1', changedAt: item.createdAt, note: null },
  ]
  if (item.status !== 'New') {
    history.push({
      from: 'New',
      to: item.status,
      changedBy: 'u-1',
      changedAt: '2026-11-20T06:00:00Z',
      note: null,
    })
  }
  return {
    order: {
      id: item.id,
      number: item.number,
      status: item.status,
      source: item.source,
      deliveryAddress: 'House 4, Road 2, Mirpur',
      isInsideDhaka: item.isInsideDhaka,
      lines: [
        {
          productVariantId: 'v-m',
          productName: 'Polo shirt',
          variantLabel: 'M / Red',
          sku: 'POLO-M',
          unitPrice: 650,
          unitCost: null,
          quantity: 2,
          lineTotal: 1300,
        },
      ],
      subtotal: 1300,
      deliveryCharge: 60,
      total: 1360,
      advancePaid: 0,
      codAmount: 1360,
      note: null,
      createdAt: item.createdAt,
      history,
    },
    customer: {
      id: item.customerId,
      phone: item.customerPhone,
      name: item.customerName,
      isBlocked: false,
      blockedReason: null,
      previousOrders: 0,
      delivered: 0,
      returned: 0,
      cancelled: 0,
    },
    warnings: [],
  }
}

/** Applies a status change like the API: a conflict leaves the order as it was. */
function changeStatus(id: string, to: OrderStatus): BulkItemResult {
  const order = api.orders.find((o) => o.id === id)
  const conflict = api.conflicts[id]
  if (!order || conflict) {
    return {
      orderId: id,
      succeeded: false,
      status: null,
      error: 'Conflict',
      message: conflict ?? 'Not found',
    }
  }
  order.status = to
  return { orderId: id, succeeded: true, status: to, error: null, message: null }
}

export interface LookupResult {
  customer: {
    id: string
    phone: string
    name: string | null
    address: string | null
    notes: null
    isBlocked: boolean
    blockedReason: string | null
    blockedAt: null
    createdAt: string
  }
  history: { previousOrders: number; delivered: number; returned: number; cancelled: number }
}

export const catalogue = [
  {
    id: 'p-1',
    name: 'Polo shirt',
    description: null,
    createdAt: '2026-11-01T00:00:00Z',
    variants: [
      { id: 'v-m', sku: 'POLO-M', size: 'M', colour: 'Red', price: 650, costPrice: null },
      { id: 'v-l', sku: 'POLO-L', size: 'L', colour: null, price: 700, costPrice: null },
    ],
  },
  {
    id: 'p-2',
    name: 'Cotton saree',
    description: null,
    createdAt: '2026-11-01T00:00:00Z',
    variants: [
      { id: 'v-s', sku: 'SAREE-1', size: null, colour: null, price: 1200, costPrice: null },
    ],
  },
]

export const validCode = '123456'

const problem = (status: number, errors?: Record<string, string[]>, detail?: string) =>
  HttpResponse.json(
    { status, title: 'Validation failed', detail, errors },
    { status, headers: { 'content-type': 'application/problem+json' } },
  )

const me = () => ({ userId: 'u-1', shopId: 's-1', role: api.role })

export const handlers = [
  http.post('*/api/auth/otp', () =>
    HttpResponse.json({ expiresInSeconds: 300, resendAfterSeconds: 30 }, { status: 202 }),
  ),

  http.post('*/api/auth/otp/verify', async ({ request }) => {
    const body = (await request.json()) as { code: string }
    if (body.code !== validCode) {
      return problem(400, {
        code: ['The code is invalid or has expired. Request a new code and try again.'],
      })
    }
    api.hasRefreshCookie = true
    return HttpResponse.json({
      ...me(),
      isNewShop: api.isNewShop,
      accessToken: 'access-1',
      accessTokenExpiresAt: '2026-11-20T10:15:00Z',
      refreshToken: null,
      refreshTokenExpiresAt: '2026-12-20T10:00:00Z',
    })
  }),

  http.post('*/api/auth/refresh', () => {
    api.refreshCalls++
    if (!api.hasRefreshCookie)
      return problem(401, undefined, 'Your session has expired. Please sign in again.')
    return HttpResponse.json({
      accessToken: `access-refreshed-${String(api.refreshCalls)}`,
      accessTokenExpiresAt: '2026-11-20T10:30:00Z',
      refreshToken: null,
      refreshTokenExpiresAt: '2026-12-20T10:00:00Z',
    })
  }),

  http.post('*/api/auth/logout', () => {
    api.hasRefreshCookie = false
    return new HttpResponse(null, { status: 204 })
  }),

  http.get('*/api/auth/me', ({ request }) =>
    request.headers.get('Authorization')?.startsWith('Bearer ')
      ? HttpResponse.json(me())
      : problem(401),
  ),

  http.get('*/api/shop', () =>
    HttpResponse.json({
      id: 's-1',
      name: api.shopName,
      logoUrl: null,
      pickupAddress: null,
      defaultCourier: null,
      deliveryChargeInsideDhaka: 60,
      deliveryChargeOutsideDhaka: 120,
      currency: 'BDT',
    }),
  ),

  http.post('*/api/customers/lookup', async ({ request }) => {
    const { phone } = (await request.json()) as { phone: string }
    const found = api.customers[phone.replace(/\D/g, '').slice(-11)]
    return found ? HttpResponse.json(found) : problem(404)
  }),

  http.get('*/api/products', ({ request }) => {
    const search = new URL(request.url).searchParams.get('Search')?.toLowerCase() ?? ''
    const items = catalogue.filter((p) => p.name.toLowerCase().includes(search))
    return HttpResponse.json({ items, page: 1, pageSize: 10, totalCount: items.length })
  }),

  http.post('*/api/orders', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>
    api.orderRequests.push({ key: request.headers.get('Idempotency-Key'), body })
    const failure = api.nextOrderFailure
    api.nextOrderFailure = null
    if (failure === 'network') return HttpResponse.error()
    if (failure) return problem(400, failure)
    return HttpResponse.json(
      {
        order: { id: `o-${String(api.orderRequests.length)}`, number: api.orderRequests.length },
        customer: {},
        warnings: [],
      },
      { status: 201 },
    )
  }),

  // Keyset paging stand-in: the cursor is the index of the next order.
  http.get('*/api/orders', ({ request }) => {
    const query = new URL(request.url).searchParams
    api.orderListQueries.push(query)
    const statuses = query.getAll('Status')
    const matching = api.orders.filter((o) => statuses.length === 0 || statuses.includes(o.status))
    const start = Number(query.get('Cursor') ?? 0)
    const end = start + Number(query.get('Limit') ?? 25)
    return HttpResponse.json({
      items: matching.slice(start, end),
      nextCursor: end < matching.length ? String(end) : null,
    })
  }),

  http.get('*/api/orders/:id', ({ params }) => {
    const order = api.orders.find((o) => o.id === params.id)
    return order ? HttpResponse.json(detailsOf(order)) : problem(404)
  }),

  http.post('*/api/orders/bulk/:action', async ({ request, params }) => {
    const { orderIds } = (await request.json()) as { orderIds: string[] }
    const to: OrderStatus = params.action === 'confirm' ? 'Confirmed' : 'Cancelled'
    const results = orderIds.map((id) => changeStatus(id, to))
    const succeeded = results.filter((r) => r.succeeded).length
    return HttpResponse.json({ succeeded, failed: results.length - succeeded, results })
  }),

  http.post('*/api/orders/:id/:action', ({ params }) => {
    const id = String(params.id)
    const result = changeStatus(id, params.action === 'confirm' ? 'Confirmed' : 'Cancelled')
    const order = api.orders.find((o) => o.id === id)
    if (!result.succeeded || !order) return problem(409, undefined, result.message ?? undefined)
    return HttpResponse.json(detailsOf(order))
  }),

  http.put('*/api/shop', async ({ request }) => {
    const body = (await request.json()) as { name: string }
    api.shopName = body.name
    return HttpResponse.json({ id: 's-1', currency: 'BDT', ...body })
  }),
]

export const server = setupServer(...handlers)

export function resetApi(): void {
  api.hasRefreshCookie = false
  api.isNewShop = true
  api.refreshCalls = 0
  api.shopName = 'My shop'
  api.customers = {}
  api.orderRequests = []
  api.nextOrderFailure = null
  api.role = 'Owner'
  api.orders = []
  api.orderListQueries = []
  api.conflicts = {}
}
