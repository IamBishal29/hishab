import { apiUrl, getAccessToken, refreshAccessToken } from '@/features/auth/session'

/** An RFC 7807 ProblemDetails response from the API. */
export class ApiProblem extends Error {
  readonly status: number
  readonly title: string | undefined
  readonly detail: string | undefined
  /** Field errors keyed by camelCase field name (FluentValidation / model binding). */
  readonly errors: Record<string, string[]>

  constructor(status: number, body: Partial<ProblemBody>) {
    super(body.detail ?? body.title ?? `Request failed with status ${String(status)}`)
    this.name = 'ApiProblem'
    this.status = status
    this.title = body.title
    this.detail = body.detail
    this.errors = body.errors ?? {}
  }
}

interface ProblemBody {
  title: string
  detail: string
  errors: Record<string, string[]>
}

/** Called when the session cannot be renewed (refresh failed): the app shows the login page. */
let onSessionExpired: () => void = () => undefined
export const setSessionExpiredHandler = (handler: () => void): void => {
  onSessionExpired = handler
}

const send = (url: string, options: RequestInit): Promise<Response> => {
  const headers = new Headers(options.headers)
  const token = getAccessToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)
  return fetch(apiUrl(url), { ...options, headers, credentials: 'same-origin' })
}

/**
 * The orval mutator: every generated hook calls the API through here. Adds the in-memory
 * access token, renews it once on 401 (never for /api/auth/* calls, so a failed refresh cannot
 * loop), and turns error responses into {@link ApiProblem}.
 */
export async function apiFetch<T>(url: string, options: RequestInit = {}): Promise<T> {
  let response = await send(url, options)

  if (response.status === 401 && !url.startsWith('/api/auth/')) {
    if (await refreshAccessToken()) {
      response = await send(url, options)
    } else {
      onSessionExpired()
    }
  }

  if (!response.ok) {
    const body = response.headers.get('content-type')?.includes('json')
      ? ((await response.json()) as Partial<ProblemBody>)
      : {}
    throw new ApiProblem(response.status, body)
  }

  // Files (PDF labels) come back as a Blob; everything else is JSON or empty.
  const type = response.headers.get('content-type') ?? ''
  if (type && !type.includes('json')) {
    return (await response.blob()) as T
  }

  const text = await response.text()
  return (text ? JSON.parse(text) : undefined) as T
}

export default apiFetch
