/**
 * The access token lives only in this module's memory: no localStorage, so an injected script
 * cannot read a stored token, and a reload simply asks /api/auth/refresh again. The refresh
 * token is an HttpOnly cookie (Path=/api/auth) that JavaScript never sees.
 */
let accessToken: string | null = null
let refreshInFlight: Promise<boolean> | null = null

export const getAccessToken = (): string | null => accessToken

export function setAccessToken(token: string | null): void {
  accessToken = token
}

/** Resolves a path against the page origin (also needed by fetch under jsdom in tests). */
export const apiUrl = (path: string): string => new URL(path, window.location.origin).toString()

/**
 * Exchanges the refresh cookie for a new access token. Single-flight: concurrent callers share
 * one request, because the API treats a second use of the same refresh token as theft and
 * revokes the whole session (#6).
 */
export function refreshAccessToken(): Promise<boolean> {
  refreshInFlight ??= (async () => {
    try {
      const response = await fetch(apiUrl('/api/auth/refresh'), {
        method: 'POST',
        credentials: 'same-origin',
      })
      if (!response.ok) {
        accessToken = null
        return false
      }
      const body = (await response.json()) as { accessToken: string }
      accessToken = body.accessToken
      return true
    } catch {
      accessToken = null
      return false
    } finally {
      refreshInFlight = null
    }
  })()
  return refreshInFlight
}
