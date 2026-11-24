import type { ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import {
  Bar,
  BarChart,
  CartesianGrid,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { formatMoney } from '@/lib/money'
import { pointLabel, type ProfitPoint } from './profitSeries'

// One series (profit), so no legend: the section title names it. Colour checked with the
// dataviz validator (contrast >= 3:1 on the surface); losses read by position below the zero line.
const SERIES = '#2a78d6'

/** Recharts is large: this module is loaded only when an owner opens the dashboard. */
export default function ProfitChart({ points }: { points: ProfitPoint[] }): ReactElement {
  const { t, i18n } = useTranslation()
  const money = (v: number) => formatMoney(v, i18n.language)
  const compact = (v: number) =>
    new Intl.NumberFormat(i18n.language === 'bn' ? 'bn-BD' : 'en-BD', {
      notation: 'compact',
    }).format(v)

  return (
    <div className="h-56 w-full" aria-hidden="true">
      {/* Hidden from screen readers: the caption and the table below carry the same numbers. */}
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={points} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
          <CartesianGrid vertical={false} stroke="#e7e6e2" />
          <XAxis
            dataKey="key"
            tickFormatter={(k: string) => pointLabel(k, i18n.language)}
            tick={{ fontSize: 11, fill: '#52514e' }}
            tickLine={false}
            axisLine={false}
            minTickGap={16}
          />
          <YAxis
            width={44}
            tickFormatter={compact}
            tick={{ fontSize: 11, fill: '#52514e' }}
            tickLine={false}
            axisLine={false}
          />
          <ReferenceLine y={0} stroke="#52514e" />
          <Tooltip
            cursor={{ fill: 'rgba(0,0,0,0.04)' }}
            formatter={(value) => [money(Number(value)), t('dashboard.profit')]}
            labelFormatter={(k) => (typeof k === 'string' ? pointLabel(k, i18n.language) : '')}
          />
          <Bar
            dataKey="profit"
            fill={SERIES}
            maxBarSize={24}
            radius={[4, 4, 0, 0]}
            isAnimationActive={false}
          />
        </BarChart>
      </ResponsiveContainer>
    </div>
  )
}
