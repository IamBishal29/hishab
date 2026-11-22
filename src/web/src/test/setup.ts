import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, beforeAll, vi } from 'vitest'
import { setAccessToken } from '@/features/auth/session'
import i18n from '@/i18n'
import { queryClient } from '@/lib/queryClient'
import { resetApi, server } from './server'

// jsdom has no object URLs; tests that save files spy on these (test/downloads.ts).
if (!('createObjectURL' in URL)) {
  Object.assign(URL, { createObjectURL: () => 'blob:test', revokeObjectURL: () => undefined })
}

// Any request without a handler fails the test: no silent calls to a real network.
beforeAll(() => {
  server.listen({ onUnhandledFrame: 'error' })
})

afterEach(async () => {
  cleanup()
  vi.restoreAllMocks()
  server.resetHandlers()
  resetApi()
  setAccessToken(null)
  queryClient.clear()
  await i18n.changeLanguage('en')
})

afterAll(() => {
  server.close()
})
