import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import App from '@/App'
import { routes } from '@/app/routes'
import { formatMoney } from '@/lib/money'
import { api, type LookupResult } from '@/test/server'

const knownPhone = '01712345678'

function knownCustomer(overrides: Partial<LookupResult['customer']> = {}): LookupResult {
  return {
    customer: {
      id: 'c-1',
      phone: '+8801712345678',
      name: 'Rahima Begum',
      address: 'House 4, Road 2, Mirpur',
      notes: null,
      isBlocked: false,
      blockedReason: null,
      blockedAt: null,
      createdAt: '2026-11-01T00:00:00Z',
      ...overrides,
    },
    history: { previousOrders: 3, delivered: 2, returned: 1, cancelled: 0 },
    risk: { level: 'Watch', reasons: ['ElevatedReturnRate', 'RecentReturn'], returnRate: 0.33 },
  }
}

async function openForm() {
  api.hasRefreshCookie = true // signed in (session restored from the refresh cookie)
  const router = createMemoryRouter(routes, { initialEntries: ['/orders/new'] })
  render(<App router={router} />)
  return await screen.findByLabelText('Customer mobile')
}

const user = () => userEvent.setup()

function summaryValue(label: string): string {
  const term = within(screen.getByLabelText('Order summary')).getByText(label)
  return term.nextElementSibling?.textContent ?? ''
}

async function addProduct(search: string, option: RegExp) {
  await user().type(screen.getByLabelText('Add product'), search)
  await user().click(await screen.findByRole('button', { name: option }))
}

describe('QuickOrderPage', () => {
  it('auto-fills a known customer after the phone is typed and shows their history', async () => {
    api.customers[knownPhone] = knownCustomer()
    const phone = await openForm()

    await user().type(phone, '+880 1712-345678') // any spelling finds them

    expect(
      await screen.findByText(/Earlier orders: 3 · delivered 2 · returned 1/),
    ).toBeInTheDocument()
    expect(screen.getByLabelText('Customer name')).toHaveValue('Rahima Begum')
    expect(screen.getByLabelText('Delivery address')).toHaveValue('House 4, Road 2, Mirpur')
  })

  it('warns about a blocked customer but still allows the order', async () => {
    api.customers[knownPhone] = knownCustomer({
      isBlocked: true,
      blockedReason: 'Refused 3 parcels',
    })
    const phone = await openForm()

    await user().type(phone, knownPhone)

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Blocked customer: Refused 3 parcels',
    )
    expect(screen.getByRole('button', { name: 'Save order' })).toBeEnabled()
  })

  it('never overwrites what the moderator already typed', async () => {
    api.customers[knownPhone] = knownCustomer()
    await openForm()

    await user().type(screen.getByLabelText('Customer name'), 'Rahima Apa')
    await user().type(screen.getByLabelText('Customer mobile'), knownPhone)

    expect(await screen.findByText(/Earlier orders: 3/)).toBeInTheDocument()
    expect(screen.getByLabelText('Customer name')).toHaveValue('Rahima Apa')
    expect(screen.getByLabelText('Delivery address')).toHaveValue('House 4, Road 2, Mirpur') // empty, so filled
  })

  it('says a new number is a new customer', async () => {
    const phone = await openForm()

    await user().type(phone, '01898765432')

    expect(await screen.findByText(/New customer/)).toBeInTheDocument()
  })

  it('validates before calling the API', async () => {
    const phone = await openForm()

    await user().type(phone, '12345')
    await user().click(screen.getByRole('button', { name: 'Save order' }))

    expect(await screen.findByText(/Enter a Bangladeshi mobile number/)).toBeInTheDocument()
    expect(screen.getByText('Add at least one product.')).toBeInTheDocument()
    expect(api.orderRequests).toHaveLength(0)
  })

  it('previews totals, saves with an idempotency key and gets ready for the next order', async () => {
    const phone = await openForm()
    await user().type(phone, '01898765432')
    await addProduct('polo', /Polo shirt · M \/ Red/)
    await addProduct('polo', /Polo shirt · M \/ Red/) // the same product again: quantity 2
    await addProduct('saree', /Cotton saree/)

    expect(summaryValue('Products')).toBe(formatMoney(2 * 650 + 1200, 'en'))
    expect(summaryValue('Delivery')).toBe(formatMoney(60, 'en')) // shop rate inside Dhaka
    await user().click(screen.getByLabelText('Outside Dhaka'))
    expect(summaryValue('Delivery')).toBe(formatMoney(120, 'en'))
    await user().type(screen.getByLabelText('Advance paid (BDT)'), '{Backspace}500')
    expect(summaryValue('Cash to collect')).toBe(formatMoney(2500 + 120 - 500, 'en'))

    await user().click(screen.getByRole('button', { name: 'Save order' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Order #1 saved')
    const [sent] = api.orderRequests
    expect(sent?.key).toMatch(/^[0-9a-f-]{36}$/)
    expect(sent?.body).toMatchObject({
      phone: '01898765432',
      isInsideDhaka: false,
      source: 'Facebook',
      advancePaid: 500,
      deliveryCharge: null,
      lines: [
        { variantId: 'v-m', quantity: 2 },
        { variantId: 'v-s', quantity: 1 },
      ],
    })
    expect(screen.getByLabelText('Customer mobile')).toHaveValue('')
    expect(screen.getByLabelText('Customer mobile')).toHaveFocus()
  })

  it('retries a lost request with the same key, and the next order gets a new one', async () => {
    const phone = await openForm()
    await user().type(phone, '01898765432')
    await addProduct('saree', /Cotton saree/)
    api.nextOrderFailure = 'network'

    await user().click(screen.getByRole('button', { name: 'Save order' }))
    expect(await screen.findByText(/Could not reach Hishab/)).toBeInTheDocument()
    await user().click(screen.getByRole('button', { name: 'Save order' }))
    expect(await screen.findByRole('status')).toHaveTextContent('saved')

    await user().type(screen.getByLabelText('Customer mobile'), '01898765433')
    await addProduct('saree', /Cotton saree/)
    await user().click(screen.getByRole('button', { name: 'Save order' }))
    await screen.findByText(/Order #3 saved/)

    const [lost, retried, next] = api.orderRequests.map((r) => r.key)
    expect(retried).toBe(lost) // the server would replay the first order, not create a second
    expect(next).not.toBe(lost)
  })

  it('shows server field errors under the line they belong to', async () => {
    const phone = await openForm()
    await user().type(phone, '01898765432')
    await addProduct('saree', /Cotton saree/)
    api.nextOrderFailure = {
      'lines[0].variantId': ['This product is not in your catalogue (it may have been deleted).'],
    }

    await user().click(screen.getByRole('button', { name: 'Save order' }))

    expect(await screen.findByText(/not in your catalogue/)).toBeInTheDocument()
    // A 4xx means nothing was created: after a fix, the next attempt uses a fresh key.
    await user().click(screen.getByRole('button', { name: 'Save order' }))
    await screen.findByRole('status')
    expect(api.orderRequests[1]?.key).not.toBe(api.orderRequests[0]?.key)
  })
})
