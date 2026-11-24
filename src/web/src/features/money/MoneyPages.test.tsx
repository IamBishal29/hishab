import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import App from '@/App'
import { getGetApiMoneyCodSummaryQueryKey, getGetApiOrdersQueryKey } from '@/api/generated/hishab'
import { routes } from '@/app/routes'
import { formatMoney } from '@/lib/money'
import { queryClient } from '@/lib/queryClient'
import { money, recordedPayout } from '@/test/moneyApi'
import { api } from '@/test/server'

function open(url: string) {
  api.hasRefreshCookie = true
  render(<App router={createMemoryRouter(routes, { initialEntries: [url] })} />)
}

const user = () => userEvent.setup()
// Intl puts a no-break space after "BDT"; Testing Library compares with normalised whitespace.
const tk = (amount: number) => formatMoney(amount, 'en').replace(/\s/g, ' ')

describe('COD summary', () => {
  it('shows what each courier owes, aged, and flags payouts that need a look', async () => {
    recordedPayout({ reference: 'PTH-OK' })
    money.unknown = ['DH-X']
    recordedPayout({
      reference: 'PTH-ODD',
      lines: [{ consignmentId: 'DH-X', collected: 500, deliveryFee: 0, codFee: 0 }],
      netAmount: 500,
    })
    open('/money')

    const totals = within(await screen.findByLabelText('COD totals'))
    expect(totals.getByText('With couriers').nextElementSibling).toHaveTextContent(tk(2000))
    const pathao = within(screen.getByRole('article', { name: 'Pathao' }))
    expect(pathao.getByText('2 parcels unpaid · oldest 16 days')).toBeInTheDocument()
    expect(pathao.getByRole('row', { name: /15\+ days/ })).toHaveTextContent(tk(1200))
    expect(pathao.getByText(`1 parcel short-paid by ${tk(50)} in total`)).toBeInTheDocument()

    const payouts = within(await screen.findByRole('list', { name: 'Payouts' }))
    expect(payouts.getByRole('link', { name: /PTH-ODD.*Needs a look/ })).toHaveAttribute(
      'href',
      '/money/payouts/pay-2',
    )
    expect(payouts.getByRole('link', { name: /PTH-OK/ })).not.toHaveTextContent('Needs a look')
  })

  it.each(['Manager', 'Moderator', 'Packer'])(
    'is hidden from a %s and never asks the API',
    async (role) => {
      api.role = role
      open('/')
      expect(await screen.findByRole('link', { name: 'Orders' })).toBeInTheDocument()
      expect(screen.queryByRole('link', { name: 'Money' })).not.toBeInTheDocument()
    },
  )

  it('tells staff without profit access that money is owner-only, without loading it', async () => {
    api.role = 'Manager'
    open('/money')
    expect(
      await screen.findByText('Only the shop owner can see money and payouts.'),
    ).toBeInTheDocument()
    expect(money.summaryCalls).toBe(0)
  })
})

describe('Record payout', () => {
  async function fillHeader() {
    await user().selectOptions(await screen.findByLabelText('Courier'), 'Pathao')
    await user().type(screen.getByLabelText('Statement or transfer ID'), 'PTH-77')
    await user().type(screen.getByLabelText('Amount received'), '1830')
  }

  it('adds pasted statement rows, checks them against the transfer and settles', async () => {
    queryClient.setQueryData(getGetApiMoneyCodSummaryQueryKey(), money.summary)
    queryClient.setQueryData(getGetApiOrdersQueryKey(), { items: [] })
    open('/money/payouts/new')
    await fillHeader()

    await user().click(screen.getByLabelText("Paste from the courier's statement"))
    await user().paste('ID\tAmount\tDelivery\tCOD\nDH-1\t1,000\t60\t10\nDH-2\t900\nTotal\tlots')
    await user().click(screen.getByRole('button', { name: 'Add pasted rows' }))

    expect(screen.getByRole('status')).toHaveTextContent(
      'Added 2 parcels. Could not read line(s) 4: check them and add them by hand.',
    )
    expect(screen.getAllByRole('group', { name: /Parcel \d/ })).toHaveLength(2) // placeholder replaced
    const check = within(screen.getByLabelText('Check against the transfer'))
    expect(check.getByText('Statement net (2 parcels)').nextElementSibling).toHaveTextContent(
      tk(1830),
    )
    expect(check.getByText('Difference').nextElementSibling).toHaveTextContent(tk(0))

    await user().click(screen.getByRole('button', { name: 'Save payout' }))

    expect(await screen.findByRole('heading', { name: 'PTH-77' })).toBeInTheDocument()
    expect(screen.getByText('Every parcel matched and settled.')).toBeInTheDocument()
    expect(money.requests).toEqual([
      {
        courier: 'Pathao',
        reference: 'PTH-77',
        paidOn: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/) as string,
        netAmount: 1830,
        note: null,
        lines: [
          { consignmentId: 'DH-1', collected: 1000, deliveryFee: 60, codFee: 10 },
          { consignmentId: 'DH-2', collected: 900, deliveryFee: 0, codFee: 0 },
        ],
      },
    ])
    // Settling moved orders to CodSettled: the summary and order lists must refetch.
    expect(queryClient.getQueryState(getGetApiMoneyCodSummaryQueryKey())?.isInvalidated).toBe(true)
    expect(queryClient.getQueryState(getGetApiOrdersQueryKey())?.isInvalidated).toBe(true)
  })

  it('rejects amounts with more than two decimals before sending', async () => {
    open('/money/payouts/new')
    await fillHeader()
    await user().type(screen.getByLabelText('Consignment ID'), 'DH-1')
    await user().type(screen.getByLabelText('Collected'), '10.555')

    await user().click(screen.getByRole('button', { name: 'Save payout' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Enter an amount like 1250 or 1250.50',
    )
    expect(money.requests).toHaveLength(0)
  })

  it("shows the server's line errors under the line and a repeated statement as a form error", async () => {
    open('/money/payouts/new')
    await fillHeader()
    await user().type(screen.getByLabelText('Consignment ID'), 'DH-1')
    await user().type(screen.getByLabelText('Collected'), '1830')

    money.nextFailure = { status: 400, errors: { 'lines[0].consignmentId': ['Too long.'] } }
    await user().click(screen.getByRole('button', { name: 'Save payout' }))
    const parcel = within(screen.getByRole('group', { name: 'Parcel 1' }))
    expect(await parcel.findByRole('alert')).toHaveTextContent('Too long.')

    money.nextFailure = { status: 409, detail: 'The Pathao payout "PTH-77" is already recorded.' }
    await user().click(screen.getByRole('button', { name: 'Save payout' }))
    expect(
      await screen.findByText('The Pathao payout "PTH-77" is already recorded.'),
    ).toBeInTheDocument()
  })
})

describe('Payout detail', () => {
  it('lists mismatched parcels first and settles them on a re-match', async () => {
    money.unknown = ['DH-9']
    recordedPayout({
      reference: 'PTH-9',
      netAmount: 1500,
      lines: [
        { consignmentId: 'DH-1', collected: 1000, deliveryFee: 0, codFee: 0 },
        { consignmentId: 'DH-9', collected: 500, deliveryFee: 0, codFee: 0 },
      ],
    })
    open('/money/payouts/pay-1')

    const flagged = within(await screen.findByRole('list', { name: '1 parcel needs a look' }))
    expect(flagged.getByText('DH-9')).toBeInTheDocument()
    expect(flagged.getByText('No booked parcel with this ID')).toBeInTheDocument()
    expect(flagged.queryByText('DH-1')).not.toBeInTheDocument()

    money.unknown = [] // the parcel was booked in Hishab since
    await user().click(screen.getByRole('button', { name: 'Check again' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Checked again: 1 more parcel settled.',
    )
    expect(screen.getByText('Every parcel matched and settled.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Check again' })).not.toBeInTheDocument()
  })
})
