import { useRef } from 'react'
import { ApiProblem } from '@/api/http'

/**
 * One key per order attempt. It survives retries whose outcome is unknown (network error,
 * 5xx: the order may exist), so a second tap cannot create a second order. It is dropped once
 * the server has answered for sure: success (next order needs a new key) or a 4xx (nothing
 * was created, and the corrected form is a different request).
 */
export function useIdempotencyKey() {
  const key = useRef<string | null>(null)
  return {
    current(): string {
      key.current ??= crypto.randomUUID()
      return key.current
    },
    settle(error?: unknown): void {
      const unknownOutcome =
        error !== undefined && !(error instanceof ApiProblem && error.status < 500)
      if (!unknownOutcome) key.current = null
    },
  }
}
