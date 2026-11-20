import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'

/** A fake Hishab API for component tests. `api` holds the state tests arrange and inspect. */
export const api = {
  /** Whether the browser holds a valid refresh cookie. */
  hasRefreshCookie: false,
  isNewShop: true,
  refreshCalls: 0,
  shopName: 'My shop',
}

export const validCode = '123456'

const problem = (status: number, errors?: Record<string, string[]>, detail?: string) =>
  HttpResponse.json(
    { status, title: 'Validation failed', detail, errors },
    { status, headers: { 'content-type': 'application/problem+json' } },
  )

const me = { userId: 'u-1', shopId: 's-1', role: 'Owner' }

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
      ...me,
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
      ? HttpResponse.json(me)
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
}
