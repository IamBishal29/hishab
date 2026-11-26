import { expect, type APIRequestContext, type Page } from '@playwright/test'
import { nextOtp, otpCodes } from './otp'

/** The seeded demo owner (Demo environment, #37). */
export const ownerPhone = '01000000001'

/** A fresh fictional customer (demo block, outside the seeded staff and customers). */
export function newCustomerPhone(): string {
  return `01000000${String(500 + Math.floor(Math.random() * 400))}`
}

/**
 * Signs in by OTP. The API allows one code per phone every 30 s and 3 per 15 min (OtpSendPolicy): a CI retry
 * right after a failure waits out the Retry-After the API sends instead of failing for an unrelated reason.
 */
export async function signIn(page: Page): Promise<void> {
  await page.goto('/login')
  const seen = otpCodes(ownerPhone).length
  await page.getByLabel('Mobile number').fill(ownerPhone)
  for (let attempt = 1; ; attempt++) {
    const sent = page.waitForResponse(
      (r) => r.url().endsWith('/api/auth/otp') && r.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Send code' }).click()
    const response = await sent
    if (response.status() !== 429) break
    const wait = Number(response.headers()['retry-after'] ?? '30')
    if (attempt > 1 || wait > 60) {
      throw new Error(
        `OTP limit for the demo owner: retry in ${String(wait)} s (3 codes per 15 min per phone)`,
      )
    }
    await page.waitForTimeout((wait + 1) * 1000) // the API said exactly how long; not a guess
  }
  await page.getByLabel('6-digit code').fill(await nextOtp(ownerPhone, seen))
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByRole('heading', { name: /Welcome/ })).toBeVisible()
}

/** A fresh access token from the shared refresh cookie (the page keeps its own only in memory). */
export async function accessToken(request: APIRequestContext): Promise<string> {
  const refreshed = await request.post('/api/auth/refresh')
  expect(refreshed.ok()).toBeTruthy()
  return ((await refreshed.json()) as { accessToken: string }).accessToken
}

/**
 * Plays the Fake courier through the dev-only endpoint (Demo environment). The access token lives only in
 * the page's memory, so this asks /api/auth/refresh with the shared cookie, as the app itself would.
 */
export async function courierMoves(request: APIRequestContext, orderId: string, status: string) {
  const moved = await request.post(`/api/dev/fake-courier/orders/${orderId}/status`, {
    headers: { Authorization: `Bearer ${await accessToken(request)}` },
    data: { status },
  })
  expect(moved.ok()).toBeTruthy()
  return (await moved.json()) as {
    order: { status: string; codAmount: number }
    shipment: { consignmentId: string; deliveryFee: number }
  }
}
