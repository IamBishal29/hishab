import { expect, test } from '@playwright/test'
import { courierMoves, newCustomerPhone, signIn } from '../support/demo'

/** A fresh fictional customer per run. */
const customerPhone = newCustomerPhone()

test.beforeEach(async ({ context }) => {
  // The app opens in Bangla for first visits; this journey reads English labels.
  await context.addInitScript(() => localStorage.setItem('hishab.language', 'en'))
})

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
