import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { setAccessToken } from '@/features/auth/session'
import i18n from '@/i18n'
import { queryClient } from '@/lib/queryClient'
import { resetApi, server } from './server'

// Any request without a handler fails the test: no silent calls to a real network.
beforeAll(() => {
  server.listen({ onUnhandledFrame: 'error' })
})

afterEach(async () => {
  cleanup()
  server.resetHandlers()
  resetApi()
  setAccessToken(null)
  queryClient.clear()
  await i18n.changeLanguage('en')
})

afterAll(() => {
  server.close()
})
