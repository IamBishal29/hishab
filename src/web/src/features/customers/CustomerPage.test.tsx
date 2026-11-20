import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import App from '@/App'
import { getGetApiOrdersIdQueryKey, getGetApiOrdersQueryKey } from '@/api/generated/hishab'
import { routes } from '@/app/routes'
import { formatMoney } from '@/lib/money'
import { queryClient } from '@/lib/queryClient'
import { api, customerPage, listItem } from '@/test/server'

function open(url = '/customers/c-1') {
  api.hasRefreshCookie = true
  render(<App router={createMemoryRouter(routes, { initialEntries: [url] })} />)
}

const user = () => userEvent.setup()
const tile = (label: string) =>
  within(screen.getByLabelText('Track record')).getByText(label).nextElementSibling?.textContent

describe('CustomerPage', () => {
  it("shows the customer's track record and only their orders", async () => {
    api.customerPages['c-1'] = customerPage()
    api.orders = [listItem(3), listItem(2, { customerId: 'c-2' }), listItem(1)]
    open()

    expect(await screen.findByRole('heading', { name: 'Rahima Begum' })).toBeInTheDocument()
    expect(tile('Orders')).toBe('5')
    expect(tile('Delivery success')).toBe('67%') // 2 delivered of 3 that reached the door
    expect(tile('Delivered value')).toBe(formatMoney(1480, 'en'))
    const orders = within(await screen.findByRole('list', { name: 'Orders from this customer' }))
    expect(await orders.findByRole('link', { name: '#3' })).toBeInTheDocument()
    expect(orders.queryByRole('link', { name: '#2' })).not.toBeInTheDocument()
    expect(api.orderListQueries.at(-1)?.get('CustomerId')).toBe('c-1')
  })

  it('saves all details together and shows server field errors under the field', async () => {
    api.customerPages['c-1'] = customerPage()
    api.nextCustomerFailure = { notes: ['Notes must be 1000 characters or fewer.'] }
    open()
    const notes = await screen.findByLabelText('Notes (only your staff see these)')

    await user().type(notes, 'Call before delivery')
    await user().click(screen.getByRole('button', { name: 'Save details' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Notes must be 1000 characters or fewer.',
    )

    await user().click(screen.getByRole('button', { name: 'Save details' }))
    expect(await screen.findByRole('status')).toHaveTextContent('Saved.')
    expect(api.customerUpdates.at(-1)).toEqual({
      name: 'Rahima Begum',
      address: 'House 4, Road 2, Mirpur',
      notes: 'Call before delivery',
    })
  })

  it('blocks with a reason and refreshes every screen that shows this customer', async () => {
    api.customerPages['c-1'] = customerPage()
    // Screens visited earlier in the session, still cached:
    queryClient.setQueryData(['customers', 'lookup', '01712345678'], {})
    queryClient.setQueryData(getGetApiOrdersIdQueryKey('o-9'), {})
    queryClient.setQueryData(getGetApiOrdersQueryKey({ Status: ['New'] }), {})
    open()

    await user().type(
      await screen.findByLabelText('Reason for blocking (optional)'),
      'Refused 3 parcels',
    )
    await user().click(screen.getByRole('button', { name: 'Block customer' }))

    expect(await screen.findByRole('note')).toHaveTextContent(
      'Blocked: Refused 3 parcels. New orders show a warning.',
    )
    expect(screen.getByText('Blocked')).toBeInTheDocument()
    for (const key of [
      ['customers', 'lookup', '01712345678'],
      getGetApiOrdersIdQueryKey('o-9'),
      getGetApiOrdersQueryKey({ Status: ['New'] }),
    ]) {
      expect(queryClient.getQueryState(key)?.isInvalidated).toBe(true)
    }
  })

  it('unblocks a blocked customer', async () => {
    api.customerPages['c-1'] = customerPage({ isBlocked: true, blockedReason: 'Fake order' })
    open()

    await user().click(await screen.findByRole('button', { name: 'Unblock' }))

    expect(await screen.findByRole('button', { name: 'Block customer' })).toBeInTheDocument()
    expect(api.customerPages['c-1'].customer.isBlocked).toBe(false)
  })

  it('links order rows to the customer only for roles that can open customers', async () => {
    api.orders = [listItem(3)]
    open('/orders')
    expect(
      await screen.findByRole('link', { name: 'Rahima Begum · +8801712345678' }),
    ).toHaveAttribute('href', '/customers/c-1')
  })

  it('tells packers they cannot open customer pages', async () => {
    api.role = 'Packer'
    api.customerPages['c-1'] = customerPage()
    open()
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Your role cannot open customer pages.',
    )
  })

  it('explains a customer from another shop or a wrong link', async () => {
    open('/customers/c-404')
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'This customer does not exist in your shop.',
    )
  })
})
