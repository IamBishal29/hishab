import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import App from '@/App'
import type { RiskAssessment } from '@/api/generated/hishab'
import { routes } from '@/app/routes'
import i18n from '@/i18n'
import { api, customerPage, listItem } from '@/test/server'
import { RiskBadge } from './RiskBadge'

const risky: RiskAssessment = {
  level: 'Risky',
  reasons: ['HighReturnRate', 'RecentReturn'],
  returnRate: 0.667,
}

function openApp(url: string) {
  api.hasRefreshCookie = true
  render(<App router={createMemoryRouter(routes, { initialEntries: [url] })} />)
}

describe('RiskBadge', () => {
  it.each([
    ['New', 'New buyer'],
    ['Safe', 'Safe'],
    ['Watch', 'Watch'],
    ['Risky', 'Risky'],
  ] as const)('names the %s level in words, with a decorative icon', (level, word) => {
    const { container } = render(
      <RiskBadge risk={{ level, reasons: [], returnRate: null }} compact />,
    )
    expect(screen.getByText(word)).toBeInTheDocument()
    // The icon repeats the word for sighted users; screen readers get the word only.
    expect(container.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
  })

  it('lists every reason, with the return rate as a whole percentage', () => {
    render(<RiskBadge risk={risky} />)
    const badge = within(screen.getByRole('group', { name: 'Customer risk' }))
    expect(badge.getByText('Risky')).toBeInTheDocument()
    expect(badge.getAllByRole('listitem').map((li) => li.textContent)).toEqual([
      'Returned 67% of parcels',
      'Returned a parcel recently',
    ])
  })

  it('shows only the first reason in compact form', () => {
    render(<RiskBadge risk={risky} compact />)
    expect(screen.getByText('Returned 67% of parcels')).toBeInTheDocument()
    expect(screen.queryByText('Returned a parcel recently')).not.toBeInTheDocument()
  })

  it('speaks Bangla', async () => {
    await i18n.changeLanguage('bn')
    render(<RiskBadge risk={risky} compact />)
    expect(screen.getByText('ঝুঁকিপূর্ণ')).toBeInTheDocument()
  })
})

describe('Risk on screens', () => {
  it("puts each row's customer risk on the order list", async () => {
    api.orders = [
      listItem(2, { customerRisk: risky }),
      listItem(1, {
        customerId: 'c-2',
        customerRisk: { level: 'New', reasons: ['NoHistory'], returnRate: null },
      }),
    ]
    openApp('/orders')

    const risky2 = within(await screen.findByRole('listitem', { name: '#2' }))
    expect(risky2.getByText('Risky')).toBeInTheDocument()
    expect(risky2.getByText('Returned 67% of parcels')).toBeInTheDocument()
    const new1 = within(screen.getByRole('listitem', { name: '#1' }))
    expect(new1.getByText('New buyer')).toBeInTheDocument()
  })

  it('shows the risk with every reason on the customer page', async () => {
    api.customerPages['c-1'] = customerPage()
    openApp('/customers/c-1')

    const badge = within(await screen.findByRole('group', { name: 'Customer risk' }))
    expect(badge.getByText('Watch')).toBeInTheDocument()
    expect(badge.getByText('Returned 33% of parcels')).toBeInTheDocument()
  })

  it('warns while taking an order from a known buyer', async () => {
    api.customers['01712345678'] = {
      customer: {
        id: 'c-1',
        phone: '+8801712345678',
        name: 'Rahima Begum',
        address: null,
        notes: null,
        isBlocked: false,
        blockedReason: null,
        blockedAt: null,
        createdAt: '2026-11-01T00:00:00Z',
      },
      history: { previousOrders: 3, delivered: 1, returned: 2, cancelled: 0 },
      risk: risky,
    }
    openApp('/orders/new')
    await userEvent.setup().type(await screen.findByLabelText('Customer mobile'), '01712345678')

    const badge = within(await screen.findByRole('group', { name: 'Customer risk' }))
    expect(badge.getByText('Risky')).toBeInTheDocument()
    expect(badge.getByText('Returned a parcel recently')).toBeInTheDocument()
  })
})
