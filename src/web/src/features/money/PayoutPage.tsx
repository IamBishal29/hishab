import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import {
  postApiMoneyPayoutsIdRematch,
  useGetApiMoneyPayoutsId,
  type PayoutLineDto,
  type PayoutResponse,
} from '@/api/generated/hishab'
import { ApiProblem } from '@/api/http'
import { Button } from '@/components/ui/button'
import { canSeeProfit } from '@/features/auth/permissions'
import { useSession } from '@/features/auth/useSession'
import { formatDay } from '@/lib/dates'
import { formatMoney } from '@/lib/money'
import { problemMessage } from '@/lib/problemErrors'
import { afterPayoutChange, canRematch, needsLook } from './moneyQueries'
import { MoneyNotAllowed, Tile } from './MoneyParts'

export function PayoutPage(): ReactElement {
  const { t } = useTranslation()
  const { id = '' } = useParams()
  const { data: session } = useSession()
  const allowed = canSeeProfit(session?.role)
  const payout = useGetApiMoneyPayoutsId(id, { query: { enabled: allowed, retry: false } })

  if (!allowed) return <MoneyNotAllowed />
  return (
    <section className="mx-auto flex max-w-2xl flex-col gap-5">
      <Link to="/money" className="text-sm underline-offset-4 hover:underline">
        ← {t('money.back')}
      </Link>
      {payout.isPending && <p>{t('common.loading')}</p>}
      {payout.isError && (
        <p role="alert" className="text-destructive">
          {payout.error instanceof ApiProblem && payout.error.status === 404
            ? t('money.payoutNotFound')
            : t('errors.unexpected')}
        </p>
      )}
      {payout.data && <PayoutView data={payout.data} />}
    </section>
  )
}

function PayoutView({ data }: { data: PayoutResponse }): ReactElement {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const [notice, setNotice] = useState<string | null>(null)
  const { payout, lines } = data
  const money = (v: number) => formatMoney(v, i18n.language)
  const flagged = lines.filter(needsLook)

  const rematch = useMutation({
    mutationFn: () => postApiMoneyPayoutsIdRematch(payout.id),
    onSuccess: (saved) => {
      afterPayoutChange(queryClient, saved)
      const newlySettled = saved.payout.settledParcels - payout.settledParcels
      setNotice(t('money.rematched', { count: newlySettled }))
    },
  })

  return (
    <>
      <header className="flex flex-col gap-1">
        <h1 className="text-2xl font-bold">{payout.reference}</h1>
        <p className="text-muted-foreground">
          {t(`couriers.names.${payout.courier}`)} · {formatDay(payout.paidOn, i18n.language)}
        </p>
        {payout.note && <p className="text-sm">{payout.note}</p>}
      </header>

      <dl
        aria-label={t('money.payoutTotals')}
        className="grid grid-cols-2 gap-2 text-center text-sm"
      >
        <Tile label={t('money.netAmount')} value={money(payout.netAmount)} />
        <Tile label={t('money.statementNetShort')} value={money(payout.statementNet)} />
        <Tile
          label={t('money.difference')}
          value={money(payout.difference)}
          warn={payout.difference !== 0}
        />
        <Tile
          label={t('money.settledOf')}
          value={t('money.nOfM', { n: payout.settledParcels, m: lines.length })}
        />
      </dl>
      {payout.difference !== 0 && (
        <p className="text-sm text-destructive">{t('money.differenceHint')}</p>
      )}

      <section aria-labelledby="flagged-heading" className="flex flex-col gap-2">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 id="flagged-heading" className="text-lg font-semibold">
            {t('money.needsLookTitle', { count: flagged.length })}
          </h2>
          {lines.some(canRematch) && (
            <Button
              variant="outline"
              size="sm"
              disabled={rematch.isPending}
              onClick={() => {
                rematch.mutate()
              }}
            >
              {t('money.rematch')}
            </Button>
          )}
        </div>
        {notice && (
          <p role="status" className="text-sm text-green-800">
            {notice}
          </p>
        )}
        {rematch.error && (
          <p role="alert" className="text-sm text-destructive">
            {t(problemMessage(rematch.error))}
          </p>
        )}
        {flagged.length === 0 ? (
          <p className="text-sm text-muted-foreground">{t('money.allSettled')}</p>
        ) : (
          <ul aria-labelledby="flagged-heading" className="flex flex-col gap-2">
            {flagged.map((l) => (
              <LineItem key={l.sequence} line={l} />
            ))}
          </ul>
        )}
      </section>

      <details className="rounded-lg border p-3 text-sm">
        <summary className="cursor-pointer font-medium">
          {t('money.allLines', { count: lines.length })}
        </summary>
        <ul className="mt-2 flex flex-col gap-2">
          {lines.map((l) => (
            <LineItem key={l.sequence} line={l} />
          ))}
        </ul>
      </details>
    </>
  )
}

function LineItem({ line }: { line: PayoutLineDto }): ReactElement {
  const { t, i18n } = useTranslation()
  const money = (v: number) => formatMoney(v, i18n.language)
  return (
    <li className="flex flex-col gap-0.5 rounded-md border p-2 text-sm">
      <div className="flex items-baseline justify-between gap-2">
        <span className="font-mono">{line.consignmentId}</span>
        {line.orderId && line.orderNumber !== null && (
          <Link to={`/orders/${line.orderId}`} className="underline underline-offset-4">
            #{line.orderNumber}
          </Link>
        )}
      </div>
      <span className={needsLook(line) ? 'text-destructive' : 'text-muted-foreground'}>
        {t(`money.match.${line.match}`)}
      </span>
      <span className="text-xs text-muted-foreground">
        {t('money.lineAmounts', {
          collected: money(line.collected),
          fees: money(line.deliveryFee + line.codFee),
        })}
        {line.codDue !== null && ` · ${t('money.due', { amount: money(line.codDue) })}`}
        {line.shortfall !== null &&
          line.shortfall !== 0 &&
          ` · ${t('money.lineShortfall', { amount: money(line.shortfall) })}`}
      </span>
    </li>
  )
}
