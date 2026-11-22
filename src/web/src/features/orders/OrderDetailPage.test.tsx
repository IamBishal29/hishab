import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import App from '@/App'
import { routes } from '@/app/routes'
import { formatMoney } from '@/lib/money'
import { captureDownloads } from '@/test/downloads'
import { api, bookedShipment, connected, listItem } from '@/test/server'

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

  describe('courier booking', () => {
    const panel = () => within(screen.getByRole('region', { name: 'Courier' }))

    it('books a confirmed order with the default courier and shows the consignment', async () => {
      api.couriers = [connected('Fake'), connected('Pathao')]
      api.defaultCourier = 'Pathao'
      api.orders = [listItem(7, { status: 'Confirmed' })]
      openOrder('o-7')

      await user().click(await screen.findByRole('button', { name: 'Book with courier' }))

      expect(await screen.findByText('FAKE-7')).toBeInTheDocument()
      expect(api.bookings).toEqual([{ orderId: 'o-7', courier: 'Pathao' }])
      expect(screen.getAllByText('Booked').length).toBeGreaterThan(0)
      expect(panel().getByText('Pathao')).toBeInTheDocument()
      expect(panel().queryByRole('button', { name: 'Book with courier' })).not.toBeInTheDocument()
    })

    it('shows the courier refusal once and lets the user try again', async () => {
      api.couriers = [connected('Fake')]
      api.orders = [listItem(7, { status: 'Confirmed' })]
      api.refusals['o-7'] = 'Pickup area not served.'
      openOrder('o-7')

      await user().click(await screen.findByRole('button', { name: 'Book with courier' }))

      // The stored rejection (from the refetch) replaces the request's own error: shown once.
      expect(
        await panel().findByText('The courier refused this parcel: Pickup area not served.'),
      ).toBeInTheDocument()
      expect(panel().getAllByText(/Pickup area not served/)).toHaveLength(1)
      expect(panel().getByRole('button', { name: 'Book with courier' })).toBeEnabled()
    })

    it('points owners to courier settings when nothing is connected', async () => {
      api.orders = [listItem(7, { status: 'Confirmed' })]
      openOrder('o-7')

      expect(await screen.findByRole('link', { name: 'Connect a courier' })).toHaveAttribute(
        'href',
        '/settings/couriers',
      )
      expect(screen.queryByRole('button', { name: 'Book with courier' })).not.toBeInTheDocument()
    })

    it('downloads the label of a booked order', async () => {
      const downloads = captureDownloads()
      api.role = 'Packer'
      api.orders = [listItem(7, { status: 'Booked' })]
      api.shipments['o-7'] = { ...bookedShipment, id: 's-7' }
      openOrder('o-7')

      await user().click(await screen.findByRole('button', { name: 'Labels (A4)' }))

      await waitFor(() => {
        expect(downloads.map((d) => d.name)).toEqual(['labels-a4.pdf'])
      })
      expect(api.labelQueries.map((q) => q.toString())).toEqual(['Ids=o-7&Format=A4'])
    })

    it('offers moderators no booking', async () => {
      api.role = 'Moderator'
      api.couriers = [connected('Fake')]
      api.orders = [listItem(7, { status: 'Confirmed' })]
      openOrder('o-7')

      await screen.findByRole('heading', { name: 'Order #7' })
      expect(screen.queryByRole('region', { name: 'Courier' })).not.toBeInTheDocument()
    })
  })

  it('explains a missing order', async () => {
    openOrder('o-404')
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'This order does not exist or was removed.',
    )
  })
})
