import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end tests against the product as compose runs it (docker compose --profile app up --wait).
 * Few and slow on purpose: one journey per core flow; rules live in unit and integration tests.
 */
export default defineConfig({
  testDir: './tests',
  timeout: 90_000,
  expect: { timeout: 15_000 },
  fullyParallel: false, // one demo shop: journeys share its data
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0, // a retry is reported as "flaky", never hidden
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:8080',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    locale: 'en-GB',
    timezoneId: 'Asia/Dhaka',
  },
  projects: [{ name: 'mobile-chromium', use: { ...devices['Pixel 7'], browserName: 'chromium' } }],
})
