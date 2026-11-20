import { queryOptions, useQuery } from '@tanstack/react-query'
import {
  getApiAuthMe,
  postApiAuthLogout,
  type LoginResponse,
  type MeResponse,
} from '@/api/generated/hishab'
import { setSessionExpiredHandler } from '@/api/http'
import { queryClient } from '@/lib/queryClient'
import { getAccessToken, refreshAccessToken, setAccessToken } from './session'

export type Session = MeResponse | null

/** On first load the memory is empty: try the refresh cookie, then ask who we are. */
async function loadSession(): Promise<Session> {
  if (!getAccessToken() && !(await refreshAccessToken())) return null
  return getApiAuthMe()
}

/** The signed-in user, or null. One query, so every screen agrees on whether we are signed in. */
export const sessionQuery = queryOptions({
  queryKey: ['session'],
  queryFn: loadSession,
  staleTime: Infinity,
  retry: false,
})

export const useSession = () => useQuery(sessionQuery)

/** After OTP verification: keep the access token in memory and seed the session. */
export function startSession(login: LoginResponse): void {
  setAccessToken(login.accessToken)
  const me: MeResponse = { userId: login.userId, shopId: login.shopId, role: login.role }
  queryClient.setQueryData(sessionQuery.queryKey, me)
}

/** Revokes the refresh family on the server, then forgets everything cached for this user. */
export async function endSession(): Promise<void> {
  try {
    await postApiAuthLogout(null)
  } finally {
    setAccessToken(null)
    queryClient.clear()
    queryClient.setQueryData(sessionQuery.queryKey, null)
  }
}

// A refresh that fails mid-session (expired, revoked, removed staff) sends the user to login.
setSessionExpiredHandler(() => {
  setAccessToken(null)
  queryClient.setQueryData(sessionQuery.queryKey, null)
})
