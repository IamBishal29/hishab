import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type {
  BulkItemResult,
  CourierConnection,
  CourierProvider,
  CustomerDetails,
  CustomerDto,
  OrderDetails,
  OrderListItem,
  OrderStatus,
  ShipmentDto,
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
  /** Customers served by GET /api/customers/{id} and changed by PUT and block/unblock. */
  customerPages: {} as Record<string, CustomerDetails>,
  /** Every PUT /api/customers/{id} body. */
  customerUpdates: [] as Record<string, unknown>[],
  /** The next customer change fails with these field errors. */
  nextCustomerFailure: null as null | Record<string, string[]>,
  /** GET /api/couriers; PUT/DELETE change it. */
  couriers: courierList(),
  /** Every PUT /api/couriers/{provider} with the keys as sent. */
  courierPuts: [] as { provider: string; credentials: Record<string, string> }[],
  defaultCourier: null as CourierProvider | null,
  /** The latest shipment per order ID (OrderDetails.shipment). */
  shipments: {} as Record<string, ShipmentDto>,
  /** Every booking: order ID and the courier asked for. */
  bookings: [] as { orderId: string; courier: string | null }[],
  /** The courier refuses these orders' parcels with this reason. */
  refusals: {} as Record<string, string>,
  /** Query strings of every GET /api/orders/labels. */
  labelQueries: [] as URLSearchParams[],
}

/** The couriers the API registers, none connected: Fake (one key) and Pathao (two fields). */
export function courierList(): CourierConnection[] {
  return [
    {
      provider: 'Fake',
      isConnected: false,
      credentialHint: null,
      connectedAt: null,
      fields: [{ name: 'apiKey', label: 'API key' }],
    },
    {
      provider: 'Pathao',
      isConnected: false,
      credentialHint: null,
      connectedAt: null,
      fields: [
        { name: 'clientId', label: 'Client ID' },
        { name: 'clientSecret', label: 'Client secret' },
      ],
    },
  ]
}

/** A booked Fake shipment (spread and set `id` per order). */
export const bookedShipment: ShipmentDto = {
  id: 's-1',
  provider: 'Fake',
  state: 'Booked',
  consignmentId: 'FAKE-6E0EA41631',
  trackingCode: 'TRK6E0EA41631',
  deliveryFee: 60,
  error: null,
  requestedAt: '2026-11-20T07:00:00Z',
  bookedAt: '2026-11-20T07:00:01Z',
}

/** A connected courier (for booking tests). */
export function connected(provider: CourierProvider): CourierConnection {
  const courier = courierList().find((c) => c.provider === provider)
  if (!courier) throw new Error(provider)
  return {
    ...courier,
    isConnected: true,
    credentialHint: '••••7890',
    connectedAt: '2026-11-20T04:00:00Z',
  }
}

export function customerPage(overrides: Partial<CustomerDto> = {}): CustomerDetails {
  return {
    customer: {
      id: 'c-1',
      phone: '+8801712345678',
      name: 'Rahima Begum',
      address: 'House 4, Road 2, Mirpur',
      notes: null,
      isBlocked: false,
      blockedReason: null,
      blockedAt: null,
      createdAt: '2026-11-01T00:00:00Z',
      ...overrides,
    },
    history: { previousOrders: 5, delivered: 2, returned: 1, cancelled: 1 },
    deliveredValue: 1480,
  }
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
    shipment: api.shipments[item.id] ?? null,
  }
}

function changeCustomer(id: string, change: Partial<CustomerDto>) {
  const page = api.customerPages[id]
  const failure = api.nextCustomerFailure
  api.nextCustomerFailure = null
  if (!page) return problem(404)
  if (failure) return problem(400, failure)
  page.customer = { ...page.customer, ...change }
  return HttpResponse.json(page.customer)
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

/** Books like the API: confirmed orders only; a refusal is stored on a Rejected shipment. */
function bookOrder(id: string, courier: string | null): BulkItemResult {
  api.bookings.push({ orderId: id, courier })
  const order = api.orders.find((o) => o.id === id)
  const provider = (courier ?? api.defaultCourier ?? 'Fake') as CourierProvider
  const fail = (error: string, message: string): BulkItemResult => ({
    orderId: id,
    succeeded: false,
    status: null,
    error,
    message,
  })
  if (order?.status !== 'Confirmed') {
    return fail('invalidTransition', 'Only confirmed orders can be booked.')
  }
  const shipment: ShipmentDto = {
    id: `s-${id}`,
    provider,
    state: 'Booked',
    consignmentId: null,
    trackingCode: null,
    deliveryFee: null,
    error: null,
    requestedAt: '2026-11-20T07:00:00Z',
    bookedAt: null,
  }
  const refusal = api.refusals[id]
  if (refusal) {
    api.shipments[id] = { ...shipment, state: 'Rejected', error: refusal }
    return fail('courierRejected', refusal)
  }
  api.shipments[id] = {
    ...shipment,
    consignmentId: `FAKE-${String(order.number)}`,
    trackingCode: `TRK${String(order.number)}`,
    deliveryFee: 60,
    bookedAt: '2026-11-20T07:00:01Z',
  }
  order.status = 'Booked'
  return { orderId: id, succeeded: true, status: 'Booked', error: null, message: null }
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
      defaultCourier: api.defaultCourier,
      deliveryChargeInsideDhaka: 60,
      deliveryChargeOutsideDhaka: 120,
      currency: 'BDT',
    }),
  ),

  http.get('*/api/couriers', () => HttpResponse.json(api.couriers)),

  http.put('*/api/couriers/:provider', async ({ request, params }) => {
    const { credentials } = (await request.json()) as { credentials: Record<string, string> }
    api.courierPuts.push({ provider: String(params.provider), credentials })
    const index = api.couriers.findIndex((c) => c.provider === params.provider)
    const courier = api.couriers[index]
    if (!courier) return problem(400, { provider: ['Not available yet.'] })
    if (Object.values(credentials).includes('invalid')) {
      return problem(400, { credentials: ['The courier rejected these keys.'] })
    }
    const key = credentials[courier.fields[0]?.name ?? ''] ?? ''
    const updated = {
      ...courier,
      isConnected: true,
      credentialHint: `••••${key.slice(-4)}`,
      connectedAt: '2026-11-22T04:00:00Z',
    }
    api.couriers[index] = updated
    return HttpResponse.json(updated)
  }),

  http.delete('*/api/couriers/:provider', ({ params }) => {
    api.couriers = api.couriers.map((c) =>
      c.provider === params.provider
        ? { ...c, isConnected: false, credentialHint: null, connectedAt: null }
        : c,
    )
    if (api.defaultCourier === params.provider) api.defaultCourier = null
    return new HttpResponse(null, { status: 204 })
  }),

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
    const customerId = query.get('CustomerId')
    const matching = api.orders.filter(
      (o) =>
        (statuses.length === 0 || statuses.includes(o.status)) &&
        (customerId === null || o.customerId === customerId),
    )
    const start = Number(query.get('Cursor') ?? 0)
    const end = start + Number(query.get('Limit') ?? 25)
    return HttpResponse.json({
      items: matching.slice(start, end),
      nextCursor: end < matching.length ? String(end) : null,
    })
  }),

  // Before /orders/:id, which would otherwise take "labels" as an order ID.
  http.get('*/api/orders/labels', ({ request }) => {
    const query = new URL(request.url).searchParams
    api.labelQueries.push(query)
    const notBooked = query
      .getAll('Ids')
      .filter((id) => api.shipments[id]?.state !== 'Booked')
      .map((id) => `#${id.slice(2)}`)
    if (notBooked.length > 0) {
      return problem(400, {
        orderIds: [`Book these orders with a courier first: ${notBooked.join(', ')}.`],
      })
    }
    return new HttpResponse(new Blob(['%PDF-1.7'], { type: 'application/pdf' }), {
      headers: { 'content-type': 'application/pdf' },
    })
  }),

  http.get('*/api/orders/:id', ({ params }) => {
    const order = api.orders.find((o) => o.id === params.id)
    return order ? HttpResponse.json(detailsOf(order)) : problem(404)
  }),

  http.post('*/api/orders/bulk/book', async ({ request }) => {
    const { orderIds, courier } = (await request.json()) as {
      orderIds: string[]
      courier: string | null
    }
    const results = orderIds.map((id) => bookOrder(id, courier))
    const succeeded = results.filter((r) => r.succeeded).length
    return HttpResponse.json({ succeeded, failed: results.length - succeeded, results })
  }),

  http.post('*/api/orders/bulk/:action', async ({ request, params }) => {
    const { orderIds } = (await request.json()) as { orderIds: string[] }
    const to: OrderStatus = params.action === 'confirm' ? 'Confirmed' : 'Cancelled'
    const results = orderIds.map((id) => changeStatus(id, to))
    const succeeded = results.filter((r) => r.succeeded).length
    return HttpResponse.json({ succeeded, failed: results.length - succeeded, results })
  }),

  http.post('*/api/orders/:id/book', async ({ request, params }) => {
    const id = String(params.id)
    const body = (await request.json()) as { courier: string | null } | null
    const result = bookOrder(id, body?.courier ?? null)
    const order = api.orders.find((o) => o.id === id)
    if (result.error === 'courierRejected') {
      return problem(400, { courier: [result.message ?? ''] })
    }
    if (!result.succeeded || !order) return problem(409, undefined, result.message ?? undefined)
    return HttpResponse.json(detailsOf(order))
  }),

  http.post('*/api/orders/:id/:action', ({ params }) => {
    const id = String(params.id)
    const result = changeStatus(id, params.action === 'confirm' ? 'Confirmed' : 'Cancelled')
    const order = api.orders.find((o) => o.id === id)
    if (!result.succeeded || !order) return problem(409, undefined, result.message ?? undefined)
    return HttpResponse.json(detailsOf(order))
  }),

  http.get('*/api/customers/:id', ({ params }) => {
    if (api.role === 'Packer') return problem(403)
    const page = api.customerPages[String(params.id)]
    return page ? HttpResponse.json(page) : problem(404)
  }),

  http.put('*/api/customers/:id', async ({ request, params }) => {
    const body = (await request.json()) as Record<string, string | null>
    api.customerUpdates.push(body)
    return changeCustomer(String(params.id), {
      name: body.name ?? null,
      address: body.address ?? null,
      notes: body.notes ?? null,
    })
  }),

  http.post('*/api/customers/:id/block', async ({ request, params }) => {
    const { reason } = (await request.json()) as { reason: string | null }
    return changeCustomer(String(params.id), {
      isBlocked: true,
      blockedReason: reason,
      blockedAt: '2026-11-20T10:00:00Z',
    })
  }),

  http.delete('*/api/customers/:id/block', ({ params }) =>
    changeCustomer(String(params.id), { isBlocked: false, blockedReason: null, blockedAt: null }),
  ),

  http.put('*/api/shop', async ({ request }) => {
    const body = (await request.json()) as {
      name: string
      defaultCourier?: CourierProvider | null
    }
    api.shopName = body.name
    api.defaultCourier = body.defaultCourier ?? null
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
  api.customerPages = {}
  api.customerUpdates = []
  api.nextCustomerFailure = null
  api.couriers = courierList()
  api.courierPuts = []
  api.defaultCourier = null
  api.shipments = {}
  api.bookings = []
  api.refusals = {}
  api.labelQueries = []
}
