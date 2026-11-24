import type { ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import {
  useGetApiMoneyCodSummary,
  useGetApiMoneyPayouts,
  type CourierCodDto,
  type PayoutSummaryDto,
} from '@/api/generated/hishab'
import { Button } from '@/components/ui/button'
import { canSeeProfit } from '@/features/auth/permissions'
import { useSession } from '@/features/auth/useSession'
import { formatDay } from '@/lib/dates'
import { formatMoney } from '@/lib/money'
import { MoneyNotAllowed, Tile } from './MoneyParts'

/** Who owes the shop what: COD per courier with ageing, and the latest payouts. Owner only. */
export function CodSummaryPage(): ReactElement {
  const { t, i18n } = useTranslation()
  const { data: session } = useSession()
  const allowed = canSeeProfit(session?.role)
  // Not even asked for without the permission: the API would answer 403 anyway.
  const summary = useGetApiMoneyCodSummary({ query: { enabled: allowed, retry: false } })
  const payouts = useGetApiMoneyPayouts({ PageSize: 20 }, { query: { enabled: allowed } })
  const money = (amount: number) => formatMoney(amount, i18n.language)

  if (!allowed) return <MoneyNotAllowed />

  return (
    <section className="mx-auto flex max-w-2xl flex-col gap-5">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-2xl font-bold">{t('money.title')}</h1>
        <Button asChild>
          <Link to="/money/payouts/new">{t('money.recordPayout')}</Link>
        </Button>
      </header>
      {summary.isPending && <p>{t('common.loading')}</p>}
      {summary.isError && (
        <p role="alert" className="text-destructive">
          {t('errors.unexpected')}
        </p>
      )}
      {summary.data && (
        <>
          <dl aria-label={t('money.totals')} className="grid grid-cols-2 gap-2 text-center text-sm">
            <Tile label={t('money.expected')} value={money(summary.data.expected)} />
            <Tile label={t('money.collected')} value={money(summary.data.collected)} />
            <Tile label={t('money.outstanding')} value={money(summary.data.outstanding)} />
            <Tile
              label={t('money.shortfall')}
              value={money(summary.data.shortfall)}
              warn={summary.data.shortfall > 0}
            />
          </dl>
          {summary.data.couriers.length === 0 && (
            <p className="text-muted-foreground">{t('money.nothingDelivered')}</p>
          )}
          {summary.data.couriers.map((c) => (
            <CourierCard key={c.courier ?? 'none'} cod={c} />
          ))}
        </>
      )}

      <section aria-labelledby="payouts-heading" className="flex flex-col gap-2">
        <h2 id="payouts-heading" className="text-lg font-semibold">
          {t('money.payouts')}
        </h2>
        {payouts.data?.items.length === 0 && (
          <p className="text-sm text-muted-foreground">{t('money.noPayouts')}</p>
        )}
        {payouts.data && payouts.data.items.length > 0 && (
          <ul
            aria-labelledby="payouts-heading"
            className="flex flex-col divide-y rounded-lg border"
          >
            {payouts.data.items.map((p) => (
              <PayoutRow key={p.id} payout={p} />
            ))}
          </ul>
        )}
      </section>
    </section>
  )
}

function CourierCard({ cod }: { cod: CourierCodDto }): ReactElement {
  const { t, i18n } = useTranslation()
  const money = (amount: number) => formatMoney(amount, i18n.language)
  const name = cod.courier ? t(`couriers.names.${cod.courier}`) : t('money.noCourier')

  return (
    <article aria-label={name} className="flex flex-col gap-2 rounded-lg border p-4 text-sm">
      <div className="flex items-baseline justify-between gap-2">
        <h2 className="font-semibold">{name}</h2>
        <span>{t('money.owes', { amount: money(cod.outstanding) })}</span>
      </div>
      <p className="text-muted-foreground">
        {t('money.parcelsUnpaid', { count: cod.outstandingParcels })}
        {cod.oldestOutstandingDays !== null &&
          ` · ${t('money.oldest', { count: cod.oldestOutstandingDays })}`}
      </p>
      <table className="w-full text-left">
        <caption className="sr-only">{t('money.ageing', { name })}</caption>
        <thead className="text-xs text-muted-foreground">
          <tr>
            <th scope="col">{t('money.age')}</th>
            <th scope="col">{t('money.parcels')}</th>
            <th scope="col" className="text-right">
              {t('money.amount')}
            </th>
          </tr>
        </thead>
        <tbody>
          {cod.outstandingByAge.map((b) => (
            <tr
              key={b.age}
              className={b.age === 'Days15Plus' && b.parcels > 0 ? 'text-destructive' : ''}
            >
              <th scope="row" className="font-normal">
                {t(`money.ages.${b.age}`)}
              </th>
              <td>{b.parcels}</td>
              <td className="text-right">{money(b.amount)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {cod.shortfall !== 0 && (
        <p className="text-destructive">
          {t('money.shortPaid', { count: cod.shortPaidParcels, amount: money(cod.shortfall) })}
        </p>
      )}
    </article>
  )
}

function PayoutRow({ payout }: { payout: PayoutSummaryDto }): ReactElement {
  const { t, i18n } = useTranslation()
  const flagged = payout.mismatchedParcels > 0 || payout.difference !== 0
  return (
    <li>
      <Link
        to={`/money/payouts/${payout.id}`}
        className="flex items-center justify-between gap-2 px-3 py-2 text-sm hover:bg-muted"
      >
        <span className="flex flex-col">
          <span className="font-medium">{payout.reference}</span>
          <span className="text-xs text-muted-foreground">
            {t(`couriers.names.${payout.courier}`)} · {formatDay(payout.paidOn, i18n.language)}
          </span>
        </span>
        <span className="flex flex-col items-end">
          <span>{formatMoney(payout.netAmount, i18n.language)}</span>
          {flagged && <span className="text-xs text-destructive">{t('money.needsLook')}</span>}
        </span>
      </Link>
    </li>
  )
}
