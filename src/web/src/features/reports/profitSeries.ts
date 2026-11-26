import type { ProfitDayDto, ProfitFiguresDto } from '@/api/generated/hishab'

export type ProfitView = 'days' | 'months'

/** One bar: a Dhaka day or a month, with the server's figures (months = their days summed). */
export interface ProfitPoint {
  /** YYYY-MM-DD for a day, YYYY-MM for a month. */
  key: string
  profit: number
  sales: number
  delivered: number
}

/** Adds days to a YYYY-MM-DD date (calendar arithmetic in UTC: no time zone can shift the day). */
export function addDays(day: string, days: number): string {
  const d = new Date(`${day}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + days)
  return d.toISOString().slice(0, 10)
}

/**
 * The API range for a view, ending today (a Dhaka date): the last 30 days, or the last 12
 * calendar months from the 1st (at most 366 days, the API's limit).
 */
export function rangeFor(view: ProfitView, today: string): { From: string; To: string } {
  if (view === 'days') return { From: addDays(today, -29), To: today }
  const d = new Date(`${today.slice(0, 7)}-01T00:00:00Z`)
  d.setUTCMonth(d.getUTCMonth() - 11)
  return { From: d.toISOString().slice(0, 10), To: today }
}

const point = (key: string, f: ProfitFiguresDto): ProfitPoint => ({
  key,
  profit: f.profit,
  sales: f.sales,
  delivered: f.delivered,
})

/**
 * Bars for the chart. Months only add up the server's daily figures (the profit rules stay in
 * the API); amounts are rounded to paisa so float sums never show 0.30000000000000004.
 */
export function toPoints(days: ProfitDayDto[], view: ProfitView): ProfitPoint[] {
  if (view === 'days') return days.map((d) => point(d.day, d.figures))

  const months = new Map<string, ProfitPoint>()
  for (const d of days) {
    const key = d.day.slice(0, 7)
    const m = months.get(key) ?? { key, profit: 0, sales: 0, delivered: 0 }
    m.profit += d.figures.profit
    m.sales += d.figures.sales
    m.delivered += d.figures.delivered
    months.set(key, m)
  }
  const paisa = (v: number) => Math.round(v * 100) / 100
  return [...months.values()].map((m) => ({ ...m, profit: paisa(m.profit), sales: paisa(m.sales) }))
}

/** "24 Nov" for a day, "Nov 2026" for a month (labels only; the key is the calendar date). */
export function pointLabel(key: string, language: string): string {
  const isMonth = key.length === 7
  return new Intl.DateTimeFormat(language === 'bn' ? 'bn-BD' : 'en-GB', {
    timeZone: 'UTC',
    ...(isMonth ? { month: 'short', year: 'numeric' } : { day: 'numeric', month: 'short' }),
  }).format(new Date(`${isMonth ? `${key}-01` : key}T00:00:00Z`))
}
