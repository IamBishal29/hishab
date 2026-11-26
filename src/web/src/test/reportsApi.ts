import { http, HttpResponse } from 'msw'
import type {
  ProfitDayDto,
  ProfitFiguresDto,
  ProfitMissing,
  ProfitReportResponse,
} from '@/api/generated/hishab'

/** The fake reports API: tests arrange the figures and inspect the ranges asked for. */
export const reports = {
  counts: { new: 4, toBook: 3, inTransit: 7, delivered: 5, returned: 1 },
  todayProfit: 1250,
  /** Profit per day served by GET /api/reports/profit (any day not listed is 0). */
  dailyProfit: {} as Record<string, number>,
  missing: noneMissing(),
  /** Every GET /api/reports/profit query string. */
  profitQueries: [] as URLSearchParams[],
}

export function resetReports(): void {
  reports.counts = { new: 4, toBook: 3, inTransit: 7, delivered: 5, returned: 1 }
  reports.todayProfit = 1250
  reports.dailyProfit = {}
  reports.missing = noneMissing()
  reports.profitQueries = []
}

function noneMissing(): ProfitMissing {
  return {
    linesWithoutCost: 0,
    returnsWithoutCharge: 0,
    parcelsWithoutDeliveryFee: 0,
    parcelsAwaitingPayout: 0,
    any: false,
  }
}

const figures = (profit: number): ProfitFiguresDto => ({
  delivered: profit === 0 ? 0 : 1,
  returned: 0,
  sales: profit === 0 ? 0 : profit * 2,
  productCost: 0,
  deliveryFees: 0,
  returnCharges: 0,
  codFees: 0,
  profit,
})

function report(from: string, to: string, daily: (day: string) => number): ProfitReportResponse {
  const days: ProfitDayDto[] = []
  for (let d = new Date(`${from}T00:00:00Z`); d <= new Date(`${to}T00:00:00Z`);) {
    const day = d.toISOString().slice(0, 10)
    days.push({ day, figures: figures(daily(day)) })
    d.setUTCDate(d.getUTCDate() + 1)
  }
  const sum = (k: keyof ProfitFiguresDto) => days.reduce((s, d) => s + d.figures[k], 0)
  return {
    from,
    to,
    days,
    total: {
      delivered: sum('delivered'),
      returned: 0,
      sales: sum('sales'),
      productCost: 0,
      deliveryFees: 0,
      returnCharges: 0,
      codFees: 0,
      profit: sum('profit'),
    },
    missing: reports.missing,
  }
}

export const reportsHandlers = [
  http.get('*/api/reports/today', () =>
    HttpResponse.json({
      day: '2026-11-24',
      counts: reports.counts,
      profit: report('2026-11-24', '2026-11-24', () => reports.todayProfit),
    }),
  ),
  http.get('*/api/reports/profit', ({ request }) => {
    const query = new URL(request.url).searchParams
    reports.profitQueries.push(query)
    const from = query.get('From') ?? '2026-11-01'
    const to = query.get('To') ?? '2026-11-24'
    return HttpResponse.json(report(from, to, (day) => reports.dailyProfit[day] ?? 0))
  }),
]
