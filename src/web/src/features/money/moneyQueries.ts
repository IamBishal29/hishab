import type { QueryClient } from '@tanstack/react-query'
import {
  getGetApiMoneyCodSummaryQueryKey,
  getGetApiMoneyPayoutsIdQueryKey,
  getGetApiMoneyPayoutsQueryKey,
  getGetApiReportsProfitQueryKey,
  getGetApiReportsTodayQueryKey,
  PayoutLineMatch,
  type PayoutLineDto,
  type PayoutResponse,
} from '@/api/generated/hishab'
import { invalidateOrders } from '@/features/orders/orderQueries'

/**
 * Recording or re-matching a payout settles orders (Delivered -> CodSettled), so one change
 * reaches every screen built on those orders: this payout, the payout list, the COD summary,
 * the reports and the orders it touched. New screens that show money must be added here.
 */
export function afterPayoutChange(queryClient: QueryClient, saved: PayoutResponse): void {
  queryClient.setQueryData(getGetApiMoneyPayoutsIdQueryKey(saved.payout.id), saved)
  for (const queryKey of [
    getGetApiMoneyPayoutsQueryKey(),
    getGetApiMoneyCodSummaryQueryKey(),
    getGetApiReportsTodayQueryKey(),
    getGetApiReportsProfitQueryKey(),
  ]) {
    void queryClient.invalidateQueries({ queryKey })
  }
  invalidateOrders(
    queryClient,
    saved.lines.map((l) => l.orderId).filter((id): id is string => id !== null),
  )
}

/** Lines the seller should look at: everything that did not settle cleanly. */
export const needsLook = (line: PayoutLineDto): boolean => line.match !== PayoutLineMatch.Settled

/** Only these can change on a re-match (the parcel may be known or delivered by now). */
export const canRematch = (line: PayoutLineDto): boolean =>
  line.match === PayoutLineMatch.UnknownParcel || line.match === PayoutLineMatch.NotDelivered
