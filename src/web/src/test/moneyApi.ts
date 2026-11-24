import { http, HttpResponse } from 'msw'
import type {
  CodSummaryResponse,
  PayoutLineDto,
  PayoutResponse,
  RecordPayoutRequest,
} from '@/api/generated/hishab'

/** The fake money API's state: tests arrange it and inspect what was sent. */
export const money = {
  summary: codSummary(),
  /** How many times GET /api/money/cod-summary was asked (to prove refreshes and no calls). */
  summaryCalls: 0,
  /** Recorded payouts, newest first. */
  payouts: [] as PayoutResponse[],
  /** Every POST /api/money/payouts body. */
  requests: [] as RecordPayoutRequest[],
  /** Consignment IDs the API does not know (UnknownParcel) until `delivered` is set by a test. */
  unknown: [] as string[],
  /** The next POST fails with this status, field errors and detail. */
  nextFailure: null as null | {
    status: number
    errors?: Record<string, string[]>
    detail?: string
  },
}

export function resetMoney(): void {
  money.summary = codSummary()
  money.summaryCalls = 0
  money.payouts = []
  money.requests = []
  money.unknown = []
  money.nextFailure = null
}

export function codSummary(): CodSummaryResponse {
  return {
    expected: 5000,
    collected: 2950,
    shortfall: 50,
    outstanding: 2000,
    couriers: [
      {
        courier: 'Pathao',
        deliveredParcels: 5,
        expected: 5000,
        collected: 2950,
        shortfall: 50,
        shortPaidParcels: 1,
        outstanding: 2000,
        outstandingParcels: 2,
        oldestOutstandingDays: 16,
        outstandingByAge: [
          { age: 'Days0To3', parcels: 1, amount: 800 },
          { age: 'Days4To7', parcels: 0, amount: 0 },
          { age: 'Days8To14', parcels: 0, amount: 0 },
          { age: 'Days15Plus', parcels: 1, amount: 1200 },
        ],
      },
    ],
  }
}

/** What the real matcher would answer for these lines: known parcels settle, unknown ones wait. */
function decide(id: string, body: RecordPayoutRequest, recordedAt: string): PayoutResponse {
  const lines: PayoutLineDto[] = body.lines.map((l, i) => {
    const known = !money.unknown.includes(l.consignmentId)
    return {
      sequence: i + 1,
      consignmentId: l.consignmentId,
      collected: l.collected,
      deliveryFee: l.deliveryFee,
      codFee: l.codFee,
      match: known ? 'Settled' : 'UnknownParcel',
      orderId: known ? `o-${String(i + 1)}` : null,
      orderNumber: known ? i + 1 : null,
      codDue: known ? l.collected : null,
      shortfall: known ? 0 : null,
    }
  })
  const statementNet = body.lines.reduce((s, l) => s + l.collected - l.deliveryFee - l.codFee, 0)
  const settled = lines.filter((l) => l.match === 'Settled').length
  return {
    payout: {
      id,
      courier: body.courier,
      reference: body.reference,
      paidOn: body.paidOn,
      netAmount: body.netAmount,
      collected: body.lines.reduce((s, l) => s + l.collected, 0),
      deliveryFees: body.lines.reduce((s, l) => s + l.deliveryFee, 0),
      codFees: body.lines.reduce((s, l) => s + l.codFee, 0),
      statementNet,
      difference: body.netAmount - statementNet,
      settledParcels: settled,
      mismatchedParcels: lines.length - settled,
      note: body.note,
      recordedAt,
    },
    lines,
  }
}

/** Records a payout directly, as if entered earlier. */
export function recordedPayout(body: Partial<RecordPayoutRequest> = {}): PayoutResponse {
  const saved = decide(
    `pay-${String(money.payouts.length + 1)}`,
    {
      courier: 'Pathao',
      reference: 'PTH-1',
      paidOn: '2026-11-24',
      netAmount: 1000,
      note: null,
      lines: [{ consignmentId: 'DH-1', collected: 1000, deliveryFee: 0, codFee: 0 }],
      ...body,
    },
    '2026-11-24T06:00:00Z',
  )
  money.payouts.unshift(saved)
  return saved
}

export const moneyHandlers = [
  http.get('*/api/money/cod-summary', () => {
    money.summaryCalls += 1
    return HttpResponse.json(money.summary)
  }),
  http.get('*/api/money/payouts', () =>
    HttpResponse.json({
      items: money.payouts.map((p) => p.payout),
      page: 1,
      pageSize: 20,
      total: money.payouts.length,
    }),
  ),
  http.get('*/api/money/payouts/:id', ({ params }) => {
    const found = money.payouts.find((p) => p.payout.id === params.id)
    return found ? HttpResponse.json(found) : HttpResponse.json({ status: 404 }, { status: 404 })
  }),
  http.post('*/api/money/payouts', async ({ request }) => {
    const body = (await request.json()) as RecordPayoutRequest
    money.requests.push(body)
    const failure = money.nextFailure
    if (failure) {
      money.nextFailure = null
      return HttpResponse.json(
        { status: failure.status, title: 'Failed', detail: failure.detail, errors: failure.errors },
        { status: failure.status, headers: { 'content-type': 'application/problem+json' } },
      )
    }
    const saved = decide(`pay-${String(money.payouts.length + 1)}`, body, new Date().toISOString())
    money.payouts.unshift(saved)
    return HttpResponse.json(saved, { status: 201 })
  }),
  http.post('*/api/money/payouts/:id/rematch', ({ params }) => {
    const index = money.payouts.findIndex((p) => p.payout.id === params.id)
    const old = money.payouts[index]
    if (!old) return HttpResponse.json({ status: 404 }, { status: 404 })
    // Re-decide with today's knowledge (tests clear `unknown` to mean "booked by now").
    const { payout } = old
    const again = decide(
      payout.id,
      {
        ...payout,
        lines: old.lines.map(({ consignmentId, collected, deliveryFee, codFee }) => ({
          consignmentId,
          collected,
          deliveryFee,
          codFee,
        })),
      },
      payout.recordedAt,
    )
    money.payouts[index] = again
    return HttpResponse.json(again)
  }),
]
