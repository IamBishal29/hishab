import type { ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import type { BulkItemResult, OrderListItem } from '@/api/generated/hishab'
import { formatDhakaDateTime } from '@/lib/dates'
import { formatMoney } from '@/lib/money'
import { StatusBadge } from './StatusBadge'
import { RiskBadge } from '@/features/customers/RiskBadge'

interface OrderRowProps {
  order: OrderListItem
  /** Bulk selection (order list only). */
  selection?: { selected: boolean; onToggle: () => void } | undefined
  /** The outcome of the last bulk action for this order. */
  result?: BulkItemResult | undefined
  /** Show who ordered: hidden on the customer's own page. Linked when the user may open customers. */
  customer?: 'hidden' | 'text' | 'link'
}

/** One order in a list: enough to scan and act on without opening it. */
export function OrderRow({
  order,
  selection,
  result,
  customer = 'text',
}: OrderRowProps): ReactElement {
  const { t, i18n } = useTranslation()
  const who = `${order.customerName ?? order.customerPhone} · ${order.customerPhone}`
  return (
    <li className="flex gap-3 rounded-lg border p-3" aria-label={`#${String(order.number)}`}>
      {selection && (
        <input
          type="checkbox"
          className="mt-1 size-5 shrink-0"
          aria-label={t('orders.select', { number: order.number })}
          checked={selection.selected}
          onChange={selection.onToggle}
        />
      )}
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <div className="flex items-center justify-between gap-2">
          <Link
            to={`/orders/${order.id}`}
            className="font-semibold underline-offset-4 hover:underline"
          >
            #{order.number}
          </Link>
          <StatusBadge status={order.status} />
        </div>
        {customer === 'link' && (
          <Link
            to={`/customers/${order.customerId}`}
            className="truncate text-sm underline-offset-4 hover:underline"
          >
            {who}
          </Link>
        )}
        {customer === 'text' && <p className="truncate text-sm">{who}</p>}
        {customer !== 'hidden' && <RiskBadge risk={order.customerRisk} compact />}
        <p className="flex flex-wrap justify-between gap-x-3 text-sm text-muted-foreground">
          <span>
            {t('orders.items', { count: order.itemCount })} ·{' '}
            {t(order.isInsideDhaka ? 'orders.inside' : 'orders.outside')}
          </span>
          <span>{t('orders.cod', { amount: formatMoney(order.codAmount, i18n.language) })}</span>
        </p>
        <p className="text-xs text-muted-foreground">
          {formatDhakaDateTime(order.createdAt, i18n.language)}
        </p>
        {result && (
          <p
            role={result.succeeded ? undefined : 'alert'}
            className={result.succeeded ? 'text-sm text-green-800' : 'text-sm text-destructive'}
          >
            {result.succeeded && result.status
              ? t('orders.rowDone', { status: t(`status.${result.status}`) })
              : t('orders.rowFailed', { message: result.message ?? result.error ?? '' })}
          </p>
        )}
      </div>
    </li>
  )
}
