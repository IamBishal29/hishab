import { mkdirSync } from 'node:fs'
import { join } from 'node:path'
import { expect, test, type Locator, type Page } from '@playwright/test'
import { accessToken, courierMoves, newCustomerPhone, signIn } from '../support/demo'
import { writeGif, type Frame } from '../support/gif'

/**
 * Regenerates the README's screenshots and demo GIF from the compose demo shop, so they always show the
 * current UI with believable (fictional) data: `docker compose --profile app up -d --wait`, then
 * `npm run readme:media`. One sign-in for everything (the OTP limit is 3 codes per 15 minutes).
 */
const out = join(import.meta.dirname, '..', '..', 'docs', 'screenshots', 'readme')
const mobile = { width: 390, height: 844 }
const desktop = { width: 1280, height: 800 }

/** Sharmin Sultana in the demo script: returns most parcels, so the lookup shows the Risky badge. */
const riskyCustomerPhone = '01000000107'

test.beforeEach(async ({ context }) => {
  await context.addInitScript(() => localStorage.setItem('hishab.language', 'en'))
})

/**
 * Waits until the page shows data: no "Loading…" placeholder left (a state, not a sleep). Not "networkidle":
 * pages that poll (order lists refresh) never go idle.
 */
async function settled(page: Page): Promise<void> {
  await expect(page.getByText('Loading…')).toHaveCount(0)
  await page.evaluate(() => document.fonts.ready)
}

/** A caption bar for GIF frames, removed again before the next action. */
async function caption(page: Page, text: string): Promise<void> {
  await page.evaluate((t) => {
    const bar = document.createElement('div')
    bar.id = 'readme-caption'
    bar.textContent = t
    bar.style.cssText =
      'position:fixed;left:0;right:0;bottom:0;z-index:9999;padding:14px 16px;background:#111;color:#fff;font:600 17px/1.3 system-ui,sans-serif;text-align:center'
    document.body.append(bar)
  }, text)
}

async function frame(
  page: Page,
  frames: Frame[],
  text: string,
  ms = 1800,
  focus?: Locator,
): Promise<void> {
  await settled(page)
  if (focus) await focus.scrollIntoViewIfNeeded()
  await caption(page, text)
  frames.push({ png: await page.screenshot(), ms })
  await page.evaluate(() => document.getElementById('readme-caption')?.remove())
}

async function shot(page: Page, name: string, fullPage = false, startAt?: Locator): Promise<void> {
  await settled(page)
  await page.evaluate(() => window.scrollTo(0, 0))
  if (startAt) await startAt.evaluate((el) => el.scrollIntoView({ block: 'start' }))
  await page.screenshot({ path: join(out, `${name}.png`), fullPage })
}

test('README screenshots and demo GIF', async ({ page }) => {
  mkdirSync(out, { recursive: true })
  const frames: Frame[] = []
  await page.setViewportSize(mobile)

  // --- The demo GIF: one order, from a buyer's message to money in the bank (mobile) ---
  await page.goto('/login')
  await page.getByLabel('Mobile number').fill('01000000001')
  await frame(page, frames, 'Sign in with a mobile number (OTP)', 1500)
  await signIn(page)
  await frame(page, frames, "Today's orders and profit at a glance", 2000)

  // Screenshots next, while the shop holds only the seeded data (the GIF's order comes last).
  // --- Mobile screenshots ---
  await page.goto('/orders/new')
  await page.getByLabel('Customer mobile').fill(riskyCustomerPhone)
  await expect(page.getByText('Risky').first()).toBeVisible()
  await shot(page, 'mobile-quick-order-risky')

  await page.goto('/orders')
  await expect(page.getByText(/#\d+/).first()).toBeVisible()
  await shot(page, 'mobile-orders', false, page.getByText('Select all shown'))

  await page.goto('/')
  await page.getByRole('button', { name: 'বাংলা' }).click()
  await expect(page.getByRole('heading', { name: 'হিসাবে স্বাগতম' })).toBeVisible()
  await expect(page.locator('svg.recharts-surface').first()).toBeVisible()
  await shot(page, 'mobile-dashboard-bangla', true)
  await page.getByRole('button', { name: 'English' }).click()

  // --- Desktop screenshots ---
  await page.setViewportSize(desktop)
  await page.goto('/')
  await expect(page.getByRole('heading', { name: /Welcome/ })).toBeVisible()
  await expect(page.locator('svg.recharts-surface').first()).toBeVisible() // the lazily loaded chart
  await shot(page, 'desktop-dashboard')

  await page.goto('/orders')
  await expect(page.getByText(/#\d+/).first()).toBeVisible()
  await shot(page, 'desktop-orders', false, page.getByText('Select all shown'))

  // The oldest settled order: a seeded one with a four-week history (the list is newest first).
  const settled = await page.request.get('/api/orders?Status=CodSettled&Limit=50', {
    headers: { Authorization: `Bearer ${await accessToken(page.request)}` },
  })
  const oldest = ((await settled.json()) as { items: { id: string }[] }).items.at(-1)
  if (!oldest) throw new Error('The demo shop has no settled order')
  await page.goto(`/orders/${oldest.id}`)
  await expect(page.getByText('COD settled', { exact: true }).first()).toBeVisible()
  await shot(page, 'desktop-order-detail')

  await page.goto('/money')
  await expect(page.getByRole('heading', { name: 'Money' })).toBeVisible()
  await shot(page, 'desktop-money')

  // --- Back to the GIF: the order itself (mobile) ---
  await page.setViewportSize(mobile)
  await page.goto('/orders/new')
  await page.getByLabel('Customer mobile').fill(newCustomerPhone())
  await page.getByLabel('Customer name').fill('Ayesha Rahman')
  await page.getByLabel('Delivery address').fill('House 9, Road 3, Dhanmondi, Dhaka')
  await page.getByLabel('Add product').fill('Polo')
  await page
    .getByRole('list', { name: 'Matching products' })
    .getByRole('button', { name: /Polo shirt · M \/ Navy/ })
    .click()
  await frame(
    page,
    frames,
    'Take an order in 20 seconds',
    2200,
    page.getByRole('button', { name: 'Save order' }),
  )
  const created = page.waitForResponse(
    (r) => r.url().endsWith('/api/orders') && r.request().method() === 'POST',
  )
  await page.getByRole('button', { name: 'Save order' }).click()
  const { order } = (await (await created).json()) as { order: { id: string; number: number } }

  await page.goto(`/orders/${order.id}`)
  await page.getByRole('button', { name: 'Confirm order' }).click()
  await page.getByRole('button', { name: 'Book with courier' }).click()
  await expect(page.getByText('Booked', { exact: true }).first()).toBeVisible()
  await frame(
    page,
    frames,
    'Book the courier in one click',
    2200,
    page.getByText('Consignment ID').first(),
  )

  await courierMoves(page.request, order.id, 'InTransit')
  const delivered = await courierMoves(page.request, order.id, 'Delivered')
  await page.reload()
  await expect(page.getByText('Delivered', { exact: true }).first()).toBeVisible()
  await frame(page, frames, 'Courier status updates on its own', 2000)

  const collected = delivered.order.codAmount
  const fee = delivered.shipment.deliveryFee
  const codFee = Math.round(collected * 0.01 * 100) / 100
  await page.goto('/money/payouts/new')
  await page
    .getByLabel('Statement or transfer ID')
    .fill(`README-${String(order.number)}-${String(Date.now())}`)
  await page.getByLabel('Amount received').fill(String(collected - fee - codFee))
  await page
    .getByLabel("Paste from the courier's statement")
    .fill(
      `${delivered.shipment.consignmentId}\t${String(collected)}\t${String(fee)}\t${String(codFee)}`,
    )
  await page.getByRole('button', { name: 'Add pasted rows' }).click()
  await frame(
    page,
    frames,
    "Paste the courier's COD payout",
    2000,
    page.getByRole('button', { name: 'Save payout' }),
  )
  await page.getByRole('button', { name: 'Save payout' }).click()
  await expect(page).toHaveURL(/\/money\/payouts\/[0-9a-f-]+$/)

  await page.goto(`/orders/${order.id}`)
  await expect(page.getByText('COD settled', { exact: true }).first()).toBeVisible()
  await frame(page, frames, 'Matched and settled: every taka accounted for', 3200)
  writeGif(join(out, 'demo.gif'), frames)
})
