import { lazy, Suspense, useState, type ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import {
  useGetApiReportsProfit,
  useGetApiReportsToday,
  type ProfitMissing,
} from '@/api/generated/hishab'
import { Button } from '@/components/ui/button'
import { formatMoney } from '@/lib/money'
import { todayInDhaka } from '@/lib/dates'
import { pointLabel, rangeFor, toPoints, type ProfitView } from './profitSeries'

const ProfitChart = lazy(() => import('./ProfitChart'))

/** The owner's home: today's pipeline and profit over time. Callers check canSeeProfit. */
export function Dashboard(): ReactElement {
  const { t, i18n } = useTranslation()
  const today = useGetApiReportsToday()
  const money = (v: number) => formatMoney(v, i18n.language)

  return (
    <div className="flex flex-col gap-6">
      <section aria-labelledby="today-heading" className="flex flex-col gap-2">
        <h2 id="today-heading" className="text-lg font-semibold">
          {t('dashboard.today')}
        </h2>
        {today.isPending && <p>{t('common.loading')}</p>}
        {today.isError && (
          <p role="alert" className="text-destructive">
            {t('errors.unexpected')}
          </p>
        )}
        {today.data && (
          <dl className="grid grid-cols-3 gap-2 text-center text-sm">
            <Card label={t('dashboard.new')} value={today.data.counts.new} />
            <Card
              label={t('dashboard.toBook')}
              value={today.data.counts.toBook}
              to="/orders?status=Confirmed"
            />
            <Card
              label={t('dashboard.inTransit')}
              value={today.data.counts.inTransit}
              to="/orders?status=Booked&status=InTransit"
            />
            <Card label={t('dashboard.delivered')} value={today.data.counts.delivered} />
            <Card label={t('dashboard.returned')} value={today.data.counts.returned} />
            <Card
              label={t('dashboard.profitToday')}
              value={money(today.data.profit.total.profit)}
            />
          </dl>
        )}
      </section>
      <ProfitSection />
    </div>
  )
}

function Card({
  label,
  value,
  to,
}: {
  label: string
  value: number | string
  to?: string
}): ReactElement {
  const body = (
    <>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-lg font-semibold">{value}</dd>
    </>
  )
  return to ? (
    <div className="rounded-lg border hover:bg-muted">
      <Link to={to} className="flex flex-col p-2">
        {body}
      </Link>
    </div>
  ) : (
    <div className="flex flex-col rounded-lg border p-2">{body}</div>
  )
}

function ProfitSection(): ReactElement {
  const { t, i18n } = useTranslation()
  const [view, setView] = useState<ProfitView>('days')
  const report = useGetApiReportsProfit(rangeFor(view, todayInDhaka()))
  const money = (v: number) => formatMoney(v, i18n.language)
  const points = report.data ? toPoints(report.data.days, view) : []

  return (
    <section aria-labelledby="profit-heading" className="flex flex-col gap-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="profit-heading" className="text-lg font-semibold">
          {t('dashboard.profitTitle')}
        </h2>
        <div role="group" aria-label={t('dashboard.period')} className="flex gap-1">
          {(['days', 'months'] as const).map((v) => (
            <Button
              key={v}
              size="sm"
              variant={view === v ? 'default' : 'outline'}
              aria-pressed={view === v}
              onClick={() => {
                setView(v)
              }}
            >
              {t(`dashboard.views.${v}`)}
            </Button>
          ))}
        </div>
      </div>
      {report.isPending && <p>{t('common.loading')}</p>}
      {report.isError && (
        <p role="alert" className="text-destructive">
          {t('errors.unexpected')}
        </p>
      )}
      {report.data && (
        <figure className="flex flex-col gap-2">
          <figcaption className="text-sm">
            {t('dashboard.summary', {
              profit: money(report.data.total.profit),
              sales: money(report.data.total.sales),
              count: report.data.total.delivered,
            })}
          </figcaption>
          <Suspense fallback={<div className="h-56" />}>
            <ProfitChart points={points} />
          </Suspense>
          {report.data.missing.any && <MissingNote missing={report.data.missing} />}
          <details className="text-sm">
            <summary className="cursor-pointer">{t('dashboard.showTable')}</summary>
            <table className="mt-2 w-full">
              <caption className="sr-only">{t('dashboard.profitTitle')}</caption>
              <thead className="text-xs text-muted-foreground">
                <tr>
                  <th scope="col" className="text-left">
                    {t(`dashboard.periodColumn.${view}`)}
                  </th>
                  <th scope="col" className="text-right">
                    {t('dashboard.sales')}
                  </th>
                  <th scope="col" className="text-right">
                    {t('dashboard.profit')}
                  </th>
                </tr>
              </thead>
              <tbody>
                {points.map((p) => (
                  <tr key={p.key}>
                    <th scope="row" className="text-left font-normal">
                      {pointLabel(p.key, i18n.language)}
                    </th>
                    <td className="text-right">{money(p.sales)}</td>
                    <td className={`text-right ${p.profit < 0 ? 'text-destructive' : ''}`}>
                      {money(p.profit)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </details>
        </figure>
      )}
    </section>
  )
}

/** Unknown costs count as 0, so profit may look higher than it is: say which figures are missing. */
function MissingNote({ missing }: { missing: ProfitMissing }): ReactElement {
  const { t } = useTranslation()
  const parts = [
    missing.linesWithoutCost > 0 &&
      t('dashboard.missing.cost', { count: missing.linesWithoutCost }),
    missing.parcelsWithoutDeliveryFee > 0 &&
      t('dashboard.missing.deliveryFee', { count: missing.parcelsWithoutDeliveryFee }),
    missing.returnsWithoutCharge > 0 &&
      t('dashboard.missing.returnCharge', { count: missing.returnsWithoutCharge }),
    missing.parcelsAwaitingPayout > 0 &&
      t('dashboard.missing.payout', { count: missing.parcelsAwaitingPayout }),
  ].filter((p): p is string => typeof p === 'string')

  return (
    <div
      role="note"
      className="rounded-md border border-amber-300 bg-amber-50 p-2 text-sm text-amber-900"
    >
      <p className="font-medium">{t('dashboard.missing.title')}</p>
      <ul className="list-inside list-disc">
        {parts.map((p) => (
          <li key={p}>{p}</li>
        ))}
      </ul>
    </div>
  )
}
