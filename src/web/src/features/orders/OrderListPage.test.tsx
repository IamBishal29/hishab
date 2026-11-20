import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import App from '@/App'
import { routes } from '@/app/routes'
import { api, listItem } from '@/test/server'

function openList(url = '/orders') {
  api.hasRefreshCookie = true
  const router = createMemoryRouter(routes, { initialEntries: [url] })
  render(<App router={router} />)
  return router
}

const user = () => userEvent.setup()
const row = (number: number) => screen.getByRole('listitem', { name: `#${String(number)}` })
const lastQuery = () => api.orderListQueries.at(-1)?.toString()

describe('OrderListPage', () => {
  it('reads the filters from a shared link and sends them to the API', async () => {
    api.orders = [listItem(2)]
    openList('/orders?status=New&area=outside&q=Rahima&status=Bogus')

    expect(await screen.findByRole('link', { name: '#2' })).toBeInTheDocument()
    expect(lastQuery()).toBe('Status=New&IsInsideDhaka=false&Search=Rahima&Limit=25')
    expect(screen.getByRole('button', { name: 'New' })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByLabelText('Area')).toHaveValue('outside')
    expect(screen.getByLabelText('Search orders')).toHaveValue('Rahima')
  })

  it('writes filter changes to the URL so Back and reload keep them', async () => {
    api.orders = [listItem(2)]
    const router = openList()
    await screen.findByRole('link', { name: '#2' })

    await user().click(screen.getByRole('button', { name: 'Confirmed' }))
    await user().selectOptions(screen.getByLabelText('Source'), 'Instagram')
    await user().type(screen.getByLabelText('Search orders'), '#1043{Enter}')

    expect(router.state.location.search).toBe('?status=Confirmed&source=Instagram&q=%231043')
    expect(lastQuery()).toBe('Status=Confirmed&Source=Instagram&Search=%231043&Limit=25')

    await user().click(screen.getByRole('button', { name: 'Clear filters' }))
    expect(router.state.location.search).toBe('')
  })

  it('loads the next page with the cursor from the previous one', async () => {
    api.orders = Array.from({ length: 30 }, (_, i) => listItem(30 - i))
    openList()
    await screen.findByRole('link', { name: '#30' })
    expect(screen.queryByRole('link', { name: '#1' })).not.toBeInTheDocument()

    await user().click(screen.getByRole('button', { name: 'Load more' }))

    expect(await screen.findByRole('link', { name: '#1' })).toBeInTheDocument()
    expect(lastQuery()).toBe('Limit=25&Cursor=25')
    expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument()
  })

  it('confirms the selected orders and shows each order its own result', async () => {
    api.orders = [listItem(3), listItem(2), listItem(1, { status: 'Cancelled' })]
    api.conflicts['o-1'] = 'A cancelled order cannot be confirmed.'
    openList()
    await screen.findByRole('link', { name: '#3' })

    await user().click(screen.getByLabelText('Select order #3'))
    await user().click(screen.getByLabelText('Select order #1'))
    await user().click(screen.getByRole('button', { name: 'Confirm' }))

    expect(await screen.findByRole('status')).toHaveTextContent('1 done, 1 not changed.')
    expect(within(row(3)).getByText('Now Confirmed')).toBeInTheDocument()
    expect(within(row(1)).getByRole('alert')).toHaveTextContent(
      'Not changed: A cancelled order cannot be confirmed.',
    )
    // The list refetched: the badge shows the server's new status.
    expect(await within(row(3)).findByText('Confirmed')).toBeInTheDocument()
    // Failed orders stay selected for another try; done ones are cleared.
    expect(screen.getByLabelText('Select order #1')).toBeChecked()
    expect(screen.getByLabelText('Select order #3')).not.toBeChecked()
    expect(within(row(2)).queryByText(/Now|Not changed/)).not.toBeInTheDocument()
  })

  it('asks before cancelling in bulk and does nothing if the user says no', async () => {
    api.orders = [listItem(2)]
    const ask = vi.spyOn(window, 'confirm').mockReturnValue(false)
    openList()
    await screen.findByRole('link', { name: '#2' })

    await user().click(screen.getByLabelText('Select all shown'))
    await user().click(screen.getByRole('button', { name: 'Cancel order' }))

    expect(ask).toHaveBeenCalledWith('Cancel 1 selected orders? This cannot be undone.')
    expect(api.orders[0]?.status).toBe('New')
    ask.mockRestore()
  })

  it('shows packers the list without selection or bulk actions', async () => {
    api.role = 'Packer'
    api.orders = [listItem(2)]
    openList()
    await screen.findByRole('link', { name: '#2' })

    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
  })

  it('says so when no order matches', async () => {
    openList('/orders?status=Returned')
    expect(await screen.findByText('No orders match these filters.')).toBeInTheDocument()
  })
})
