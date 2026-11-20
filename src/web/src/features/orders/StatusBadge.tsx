import type { ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import { cn } from 'cn'
import type { OrderStatus } from '@/api/generated/hishab'

const tone: Record<OrderStatus, string> = {
  New: 'bg-blue-100 text-blue-900',
  Confirmed: 'bg-indigo-100 text-indigo-900',
  Booked: 'bg-amber-100 text-amber-900',
  InTransit: 'bg-amber-100 text-amber-900',
  Delivered: 'bg-green-100 text-green-900',
  CodSettled: 'bg-green-200 text-green-950',
  Returned: 'bg-red-100 text-red-900',
  ReceivedBack: 'bg-red-50 text-red-900',
  Cancelled: 'bg-muted text-muted-foreground',
}

/** Text, not colour alone, carries the status (readable for colour-blind users). */
export function StatusBadge({ status }: { status: OrderStatus }): ReactElement {
  const { t } = useTranslation()
  return (
    <span className={cn('rounded-full px-2 py-0.5 text-xs font-medium', tone[status])}>
      {t(`status.${status}`)}
    </span>
  )
}
