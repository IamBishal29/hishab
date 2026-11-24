import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import App from '@/App'
import { routes } from '@/app/routes'
import { todayInDhaka } from '@/lib/dates'
import { formatMoney } from '@/lib/money'
import { reports } from '@/test/reportsApi'
import { api } from '@/test/server'
import { addDays, rangeFor, type ProfitPoint } from './profitSeries'

// jsdom has no layout, so Recharts cannot draw here; the stub shows what the chart was given.
// The caption and the table carry the same numbers and are what users of assistive tech read.
vi.mock('./ProfitChart', () => ({
  default: ({ points }: { points: ProfitPoint[] }) => (
    <p data-testid="chart">{points.map((p) => `${p.key}=${String(p.profit)}`).join(' ')}</p>
  ),
}))

function openHome(role = 'Owner') {
  api.role = role
  api.hasRefreshCookie = true
  render(<App router={createMemoryRouter(routes, { initialEntries: ['/'] })} />)
}

const user = () => userEvent.setup()
const tk = (v: number) => formatMoney(v, 'en').replace(/\s/g, ' ')
const card = (label: string) =>
  within(screen.getByRole('region', { name: 'Today' })).getByText(label).nextElementSibling
    ?.textContent

describe('Dashboard', () => {
  it("shows today's pipeline, with links to the orders that need work", async () => {
    openHome()

    expect(await screen.findByText('Profit today')).toBeInTheDocument()
    expect(card('New today')).toBe('4')
    expect(card('With courier')).toBe('7')
    expect(card('Profit today')?.replace(/\s/g, ' ')).toBe(tk(1250))
    expect(screen.getByRole('link', { name: /To book\s*3/ })).toHaveAttribute(
      'href',
      '/orders?status=Confirmed',
    )
  })

  it('charts the last 30 days by default and twelve months on request', async () => {
    const today = todayInDhaka()
    reports.dailyProfit = { [today]: 300, [addDays(today, -1)]: -100 }
    openHome()

    const profit = within(await screen.findByRole('region', { name: 'Profit' }))
    expect(
      await profit.findByText(`Profit ${tk(200)} from ${tk(400)} sales (2 parcels delivered).`),
    ).toBeInTheDocument()
    expect(reports.profitQueries.at(-1)?.get('From')).toBe(rangeFor('days', today).From)
    expect(await profit.findByTestId('chart')).toHaveTextContent(`${today}=300`)
    expect(profit.getByRole('button', { name: '30 days' })).toHaveAttribute('aria-pressed', 'true')

    await user().click(profit.getByRole('button', { name: '12 months' }))

    expect(profit.getByRole('button', { name: '12 months' })).toHaveAttribute(
      'aria-pressed',
      'true',
    )
    await screen.findByText((_, el) => el?.tagName === 'TH' && el.textContent === 'Month')
    expect(reports.profitQueries.at(-1)?.get('From')).toBe(rangeFor('months', today).From)
    expect(screen.getAllByRole('row')).toHaveLength(13) // header + 12 months
  })

  it('warns that profit may be overstated when costs are unknown', async () => {
    reports.missing = {
      linesWithoutCost: 2,
      returnsWithoutCharge: 0,
      parcelsWithoutDeliveryFee: 0,
      parcelsAwaitingPayout: 1,
      any: true,
    }
    openHome()

    const note = await screen.findByRole('note')
    expect(note).toHaveTextContent('2 sold items have no cost price')
    expect(note).toHaveTextContent('1 parcel awaits a payout (COD fee unknown)')
    expect(note).not.toHaveTextContent('delivery fee')
  })

  it.each(['Manager', 'Moderator', 'Packer'])(
    'is not shown to a %s, and the reports are never asked for',
    async (role) => {
      openHome(role)
      expect(await screen.findByRole('link', { name: 'Orders' })).toBeInTheDocument()
      expect(screen.queryByRole('region', { name: 'Today' })).not.toBeInTheDocument()
      expect(reports.profitQueries).toHaveLength(0)
    },
  )
})
