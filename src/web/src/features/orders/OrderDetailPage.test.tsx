import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import App from '@/App'
import { routes } from '@/app/routes'
import { formatMoney } from '@/lib/money'
import { api, listItem } from '@/test/server'

function openOrder(id: string) {
  api.hasRefreshCookie = true
  render(<App router={createMemoryRouter(routes, { initialEntries: [`/orders/${id}`] })} />)
}

const user = () => userEvent.setup()

describe('OrderDetailPage', () => {
  it('shows the lines, totals and status history oldest first', async () => {
    api.orders = [listItem(7, { status: 'Confirmed' })]
    openOrder('o-7')

    expect(await screen.findByRole('heading', { name: 'Order #7' })).toBeInTheDocument()
    expect(screen.getByText('2 × Polo shirt (M / Red)')).toBeInTheDocument()
    const summary = within(screen.getByLabelText('Order summary'))
    expect(summary.getByText('Cash to collect').nextElementSibling?.textContent).toBe(
      formatMoney(1360, 'en'),
    )
    const steps = within(screen.getByRole('list', { name: 'Status history' })).getAllByRole(
      'listitem',
    )
    expect(steps.map((s) => s.querySelector('p')?.textContent)).toEqual([
      'Created: New',
      'New → Confirmed',
    ])
  })

  it('confirms a new order and shows the new status', async () => {
    api.orders = [listItem(7)]
    openOrder('o-7')

    await user().click(await screen.findByRole('button', { name: 'Confirm order' }))

    expect(await screen.findByText('New → Confirmed')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Confirm order' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancel order' })).toBeInTheDocument()
  })

  it('shows the server reason when someone else changed the order first', async () => {
    api.orders = [listItem(7)]
    api.conflicts['o-7'] = 'This order was changed by someone else. Reload and try again.'
    openOrder('o-7')

    await user().click(await screen.findByRole('button', { name: 'Confirm order' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'This order was changed by someone else. Reload and try again.',
    )
  })

  it('offers no status actions to packers', async () => {
    api.role = 'Packer'
    api.orders = [listItem(7)]
    openOrder('o-7')

    await screen.findByRole('heading', { name: 'Order #7' })
    expect(screen.queryByRole('button', { name: /Confirm|Cancel/ })).not.toBeInTheDocument()
  })

  it('explains a missing order', async () => {
    openOrder('o-404')
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'This order does not exist or was removed.',
    )
  })
})
