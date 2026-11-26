import { defineConfig } from '@playwright/test'

/** README screenshots and demo GIF from the compose demo shop: `npm run readme:media` (not part of `npm test`). */
export default defineConfig({
  testDir: './media',
  testMatch: '*.media.ts',
  timeout: 180_000,
  expect: { timeout: 15_000 },
  workers: 1,
  reporter: 'list',
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:8080',
    browserName: 'chromium',
    locale: 'en-GB',
    timezoneId: 'Asia/Dhaka',
    deviceScaleFactor: 1,
  },
})
