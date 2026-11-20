import { http, HttpResponse } from 'msw'
import { getAccessToken, refreshAccessToken, setAccessToken } from '@/features/auth/session'
import { api, server } from '@/test/server'
import { ApiProblem, apiFetch } from './http'

describe('apiFetch', () => {
  it('renews an expired access token once and retries the request', async () => {
    api.hasRefreshCookie = true
    setAccessToken('expired')
    server.use(
      http.get('*/api/shop', ({ request }) =>
        request.headers.get('Authorization') === 'Bearer expired'
          ? new HttpResponse(null, { status: 401 })
          : HttpResponse.json({ name: 'Rahim Fashion' }),
      ),
    )

    const shop = await apiFetch<{ name: string }>('/api/shop')

    expect(shop.name).toBe('Rahim Fashion')
    expect(api.refreshCalls).toBe(1)
  })

  it('shares one refresh between parallel requests (a second refresh would look like token theft)', async () => {
    api.hasRefreshCookie = true

    const results = await Promise.all([
      refreshAccessToken(),
      refreshAccessToken(),
      refreshAccessToken(),
    ])

    expect(results).toEqual([true, true, true])
    expect(api.refreshCalls).toBe(1)
    expect(getAccessToken()).toBe('access-refreshed-1')
  })

  it('turns ProblemDetails into an ApiProblem with field errors', async () => {
    const error = await apiFetch('/api/auth/otp/verify', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ phone: '01712345678', code: '000000' }),
    }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiProblem)
    expect((error as ApiProblem).status).toBe(400)
    expect((error as ApiProblem).errors['code']?.[0]).toContain('invalid or has expired')
  })

  it('does not try to refresh after a 401 from an auth endpoint', async () => {
    await expect(apiFetch('/api/auth/refresh', { method: 'POST' })).rejects.toBeInstanceOf(
      ApiProblem,
    )
    expect(api.refreshCalls).toBe(1) // only the call itself, no extra refresh
  })
})
