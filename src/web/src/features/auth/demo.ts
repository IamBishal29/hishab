import { queryOptions, useMutation, useQuery } from '@tanstack/react-query'
import type { LoginResponse, ShopRole } from '@/api/generated/hishab'
import { ApiProblem, apiFetch } from '@/api/http'
import { startSession } from './useSession'

/**
 * GET /api/auth/demo. A dev-only route (it does not exist in production), so it is left out of the
 * OpenAPI document and the generated client on purpose: these two calls are written by hand.
 */
interface DemoInfo {
  roles: ShopRole[]
}

/** 404 = not the hosted demo (the normal case): no demo UI, and no error. */
async function loadDemo(): Promise<DemoInfo | null> {
  try {
    return await apiFetch<DemoInfo>('/api/auth/demo')
  } catch (error) {
    if (error instanceof ApiProblem && error.status === 404) return null
    throw error
  }
}

export const demoQuery = queryOptions({
  queryKey: ['demo'],
  queryFn: loadDemo,
  staleTime: Infinity,
  retry: false,
})

/** The demo's roles, or null. Fails closed: any error also hides the demo UI. */
export const useDemo = (): DemoInfo | null => useQuery(demoQuery).data ?? null

/** One-click sign-in; the refresh token arrives as the usual HttpOnly cookie. */
export const useDemoSignIn = () =>
  useMutation({
    mutationFn: (role: ShopRole) =>
      apiFetch<LoginResponse>('/api/auth/demo', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ role }),
      }),
    onSuccess: startSession,
  })
