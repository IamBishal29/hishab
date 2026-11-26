import type { ProfitDayDto } from '@/api/generated/hishab'
import { addDays, pointLabel, rangeFor, toPoints } from './profitSeries'

const day = (date: string, profit: number, sales = 0, delivered = 0): ProfitDayDto => ({
  day: date,
  figures: {
    delivered,
    returned: 0,
    sales,
    productCost: 0,
    deliveryFees: 0,
    returnCharges: 0,
    codFees: 0,
    profit,
  },
})

describe('profit series', () => {
  it('counts calendar days across month and leap-year ends', () => {
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28')
    expect(addDays('2028-03-01', -1)).toBe('2028-02-29')
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01')
  })

  it('asks for the last 30 days, or 12 whole months, ending today', () => {
    expect(rangeFor('days', '2026-11-24')).toEqual({ From: '2026-10-26', To: '2026-11-24' })
    expect(rangeFor('months', '2026-11-24')).toEqual({ From: '2025-12-01', To: '2026-11-24' })
    expect(rangeFor('months', '2027-01-31')).toEqual({ From: '2026-02-01', To: '2027-01-31' })
  })

  it("sums the server's days into months, losses included, without float noise", () => {
    const points = toPoints(
      [
        day('2026-10-30', 0.1, 100, 1),
        day('2026-10-31', 0.2, 200, 2),
        day('2026-11-01', -50, 0),
        day('2026-11-02', 20.5, 300, 1),
      ],
      'months',
    )
    expect(points).toEqual([
      { key: '2026-10', profit: 0.3, sales: 300, delivered: 3 },
      { key: '2026-11', profit: -29.5, sales: 300, delivered: 1 },
    ])
  })

  it('keeps one bar per day in the day view', () => {
    expect(toPoints([day('2026-11-01', 5)], 'days')).toEqual([
      { key: '2026-11-01', profit: 5, sales: 0, delivered: 0 },
    ])
  })

  it('labels days and months', () => {
    expect(pointLabel('2026-11-24', 'en')).toBe('24 Nov')
    expect(pointLabel('2026-11', 'en')).toBe('Nov 2026')
  })
})
