import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { nextOtp, otpCodes } from '../support/otp'

/** The seeded demo owner (Demo environment, #37). */
const ownerPhone = '01000000001'

/** A fresh fictional customer per run (demo block, outside the seeded staff and customers). */
const customerPhone = `01000000${String(500 + Math.floor(Math.random() * 400))}`

test.beforeEach(async ({ context }) => {
  // The app opens in Bangla for first visits; this journey reads English labels.
  await context.addInitScript(() => localStorage.setItem('hishab.language', 'en'))
})

/**
 * Signs in by OTP. The API allows one code per phone every 30 s and 3 per 15 min (OtpSendPolicy): a CI retry
 * right after a failure waits out the Retry-After the API sends instead of failing for an unrelated reason.
 */
async function signIn(page: Page): Promise<void> {
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

/**
 * Plays the Fake courier through the dev-only endpoint (Demo environment). The access token lives only in
 * the page's memory, so this asks /api/auth/refresh with the shared cookie, as the app itself would.
 */
async function courierMoves(request: APIRequestContext, orderId: string, status: string) {
  const refreshed = await request.post('/api/auth/refresh')
  expect(refreshed.ok()).toBeTruthy()
  const { accessToken } = (await refreshed.json()) as { accessToken: string }
  const moved = await request.post(`/api/dev/fake-courier/orders/${orderId}/status`, {
    headers: { Authorization: `Bearer ${accessToken}` },
    data: { status },
  })
  expect(moved.ok()).toBeTruthy()
  return (await moved.json()) as {
    order: { status: string; codAmount: number }
    shipment: { consignmentId: string; deliveryFee: number }
  }
}

test('an order goes from entry to booked, delivered and COD settled', async ({ page }) => {
  await signIn(page)

  // 1. Enter the order (quick order form).
  await page.goto('/orders/new')
  await page.getByLabel('Customer mobile').fill(customerPhone)
  await page.getByLabel('Customer name').fill('E2E Buyer')
  await page.getByLabel('Delivery address').fill('House 1, Road 1, Mirpur 10, Dhaka')
  await page.getByLabel('Add product').fill('Polo')
  await page
    .getByRole('list', { name: 'Matching products' })
    .getByRole('button', { name: /Polo shirt · M \/ Navy/ })
    .click()
  const created = page.waitForResponse(
    (r) => r.url().endsWith('/api/orders') && r.request().method() === 'POST',
  )
  await page.getByRole('button', { name: 'Save order' }).click()
  const { order } = (await (await created).json()) as { order: { id: string; number: number } }
  await expect(page.getByText(`Order #${String(order.number)} saved`)).toBeVisible()

  // 2. Confirm and book with the Fake courier (order detail).
  await page.goto(`/orders/${order.id}`)
  await page.getByRole('button', { name: 'Confirm order' }).click()
  await expect(page.getByText('Confirmed', { exact: true }).first()).toBeVisible()
  await page.getByRole('button', { name: 'Book with courier' }).click()
  await expect(page.getByText('Booked', { exact: true }).first()).toBeVisible()

  // 3. The courier picks it up and delivers it; the status sync moves the order.
  await courierMoves(page.request, order.id, 'InTransit')
  const delivered = await courierMoves(page.request, order.id, 'Delivered')
  expect(delivered.order.status).toBe('Delivered')
  await page.reload()
  await expect(page.getByText('Delivered', { exact: true }).first()).toBeVisible()

  // 4. The courier's weekly payout arrives: record it by pasting the statement row.
  const collected = delivered.order.codAmount
  const fee = delivered.shipment.deliveryFee
  const codFee = Math.round(collected * 0.01 * 100) / 100
  await page.goto('/money/payouts/new')
  await page
    .getByLabel('Statement or transfer ID')
    .fill(`E2E-${String(order.number)}-${String(Date.now())}`)
  await page.getByLabel('Amount received').fill(String(collected - fee - codFee))
  await page
    .getByLabel("Paste from the courier's statement")
    .fill(
      `${delivered.shipment.consignmentId}\t${String(collected)}\t${String(fee)}\t${String(codFee)}`,
    )
  await page.getByRole('button', { name: 'Add pasted rows' }).click()
  await page.getByRole('button', { name: 'Save payout' }).click()
  await expect(page).toHaveURL(/\/money\/payouts\/[0-9a-f-]+$/)

  // 5. The order is settled.
  await page.goto(`/orders/${order.id}`)
  await expect(page.getByText('COD settled', { exact: true }).first()).toBeVisible()
})
