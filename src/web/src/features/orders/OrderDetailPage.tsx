import { useMutation, useQueryClient } from '@tanstack/react-query'
import type { ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import {
  getGetApiOrdersIdQueryKey,
  postApiOrdersIdCancel,
  postApiOrdersIdConfirm,
  useGetApiOrdersId,
  type OrderDetails,
} from '@/api/generated/hishab'
import { ApiProblem } from '@/api/http'
import { Button } from '@/components/ui/button'
import { canEnterOrders } from '@/features/auth/permissions'
import { useSession } from '@/features/auth/useSession'
import { formatDhakaDateTime } from '@/lib/dates'
import { formatMoney } from '@/lib/money'
import { canCancel, canConfirm, invalidateOrders } from './orderQueries'
import { ShipmentPanel } from './ShipmentPanel'
import { StatusBadge } from './StatusBadge'

export function OrderDetailPage(): ReactElement {
  const { t } = useTranslation()
  const { id = '' } = useParams()
  const details = useGetApiOrdersId(id, { query: { retry: false } })

  return (
    <section className="mx-auto flex max-w-2xl flex-col gap-4">
      <Link to="/orders" className="text-sm underline-offset-4 hover:underline">
        ← {t('orderDetail.back')}
      </Link>
      {details.isPending && <p>{t('common.loading')}</p>}
      {details.isError && (
        <p role="alert" className="text-destructive">
          {details.error instanceof ApiProblem && details.error.status === 404
            ? t('orderDetail.notFound')
            : t('errors.unexpected')}
        </p>
      )}
      {details.data && <OrderView details={details.data} />}
    </section>
  )
}

type Action = 'confirm' | 'cancel'

function OrderView({ details }: { details: OrderDetails }): ReactElement {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const { data: session } = useSession()
  const { order, customer } = details
  const money = (amount: number) => formatMoney(amount, i18n.language)

  const change = useMutation({
    mutationFn: (action: Action) =>
      action === 'confirm' ? postApiOrdersIdConfirm(order.id) : postApiOrdersIdCancel(order.id),
    // The response is the order after the change: show it now, then let lists refetch.
    onSuccess: (updated) => {
      queryClient.setQueryData(getGetApiOrdersIdQueryKey(order.id), updated)
    },
    onSettled: () => {
      invalidateOrders(queryClient, [order.id])
    },
  })
  const run = (action: Action) => {
    if (
      action === 'cancel' &&
      !window.confirm(t('orderDetail.cancelAsk', { number: order.number }))
    )
      return
    change.mutate(action)
  }
  const showActions =
    canEnterOrders(session?.role) && (canConfirm(order.status) || canCancel(order.status))

  return (
    <>
      <div className="flex items-center justify-between gap-2">
        <h1 className="text-2xl font-bold">{t('orderDetail.title', { number: order.number })}</h1>
        <StatusBadge status={order.status} />
      </div>

      {showActions && (
        <div className="flex gap-2">
          {canConfirm(order.status) && (
            <Button
              disabled={change.isPending}
              onClick={() => {
                run('confirm')
              }}
            >
              {t('orderDetail.confirm')}
            </Button>
          )}
          {canCancel(order.status) && (
            <Button
              variant="outline"
              disabled={change.isPending}
              onClick={() => {
                run('cancel')
              }}
            >
              {t('orderDetail.cancel')}
            </Button>
          )}
        </div>
      )}
      {/* 409: someone else changed the order first; the refetch shows its real status. */}
      {change.error && (
        <p role="alert" className="text-sm text-destructive">
          {change.error instanceof ApiProblem ? change.error.message : t('errors.network')}
        </p>
      )}

      <ShipmentPanel details={details} />

      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
        <dt className="text-muted-foreground">{t('orderDetail.customer')}</dt>
        <dd>
          {canEnterOrders(session?.role) ? (
            <Link to={`/customers/${customer.id}`} className="underline underline-offset-4">
              {customer.name ?? customer.phone} · {customer.phone}
            </Link>
          ) : (
            `${customer.name ?? customer.phone} · ${customer.phone}`
          )}
          {customer.isBlocked && (
            <span className="ml-2 rounded-full bg-destructive/10 px-2 py-0.5 text-xs text-destructive">
              {t('customer.blockedBadge')}
            </span>
          )}
        </dd>
        <dt className="text-muted-foreground">{t('orderDetail.delivery')}</dt>
        <dd>
          {order.deliveryAddress ?? '—'} ·{' '}
          {t(order.isInsideDhaka ? 'orders.inside' : 'orders.outside')}
        </dd>
        <dt className="text-muted-foreground">{t('order.source')}</dt>
        <dd>{t(`order.sources.${order.source}`)}</dd>
        {order.note && (
          <>
            <dt className="text-muted-foreground">{t('orderDetail.note')}</dt>
            <dd>{order.note}</dd>
          </>
        )}
      </dl>

      <h2 className="font-semibold">{t('orderDetail.items')}</h2>
      <ul className="flex flex-col gap-1 text-sm">
        {order.lines.map((line, index) => (
          <li key={index} className="flex justify-between gap-2">
            <span>
              {line.quantity} × {line.productName}
              {line.variantLabel && ` (${line.variantLabel})`}
            </span>
            <span>{money(line.lineTotal)}</span>
          </li>
        ))}
      </ul>
      <dl
        aria-label={t('order.summary')}
        className="grid grid-cols-[1fr_auto] gap-1 border-t pt-2 text-sm"
      >
        <dt>{t('order.subtotal')}</dt>
        <dd>{money(order.subtotal)}</dd>
        <dt>{t('order.delivery')}</dt>
        <dd>{money(order.deliveryCharge)}</dd>
        <dt>{t('order.total')}</dt>
        <dd>{money(order.total)}</dd>
        <dt className="font-semibold">{t('order.cod')}</dt>
        <dd className="font-semibold">{money(order.codAmount)}</dd>
      </dl>

      <h2 className="font-semibold">{t('orderDetail.history')}</h2>
      <ol aria-label={t('orderDetail.history')} className="flex flex-col gap-3 border-l-2 pl-4">
        {order.history.map((step, index) => (
          <li key={index} className="relative text-sm">
            <span className="absolute top-1.5 -left-[1.3rem] size-2 rounded-full bg-primary" />
            <p className="font-medium">
              {step.from
                ? t('orderDetail.changed', {
                    from: t(`status.${step.from}`),
                    to: t(`status.${step.to}`),
                  })
                : `${t('orderDetail.created')}: ${t(`status.${step.to}`)}`}
            </p>
            <p className="text-xs text-muted-foreground">
              {formatDhakaDateTime(step.changedAt, i18n.language)}
            </p>
            {step.note && <p>{step.note}</p>}
          </li>
        ))}
      </ol>
    </>
  )
}
