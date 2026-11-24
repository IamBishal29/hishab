import { zodResolver } from '@hookform/resolvers/zod'
import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useId, type ReactElement } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router'
import { z } from 'zod'
import {
  deleteApiCustomersIdBlock,
  postApiCustomersIdBlock,
  putApiCustomersId,
  useGetApiCustomersId,
  type CustomerDetails,
  type CustomerDto,
} from '@/api/generated/hishab'
import { ApiProblem } from '@/api/http'
import { FormField } from '@/components/FormField'
import { Button } from '@/components/ui/button'
import { formatMoney } from '@/lib/money'
import { applyProblem } from '@/lib/problemErrors'
import { orderListQuery } from '@/features/orders/orderQueries'
import { OrderRow } from '@/features/orders/OrderRow'
import { afterCustomerChange, deliveryRate } from './customerQueries'
import { RiskBadge } from './RiskBadge'

export function CustomerPage(): ReactElement {
  const { t } = useTranslation()
  const { id = '' } = useParams()
  const details = useGetApiCustomersId(id, { query: { retry: false } })

  return (
    <section className="mx-auto flex max-w-2xl flex-col gap-5">
      <Link to="/orders" className="text-sm underline-offset-4 hover:underline">
        ← {t('orderDetail.back')}
      </Link>
      {details.isPending && <p>{t('common.loading')}</p>}
      {details.isError && (
        <p role="alert" className="text-destructive">
          {details.error instanceof ApiProblem && details.error.status === 404
            ? t('customer.notFound')
            : details.error instanceof ApiProblem && details.error.status === 403
              ? t('customer.noAccess')
              : t('errors.unexpected')}
        </p>
      )}
      {details.data && <CustomerView details={details.data} />}
    </section>
  )
}

function CustomerView({ details }: { details: CustomerDetails }): ReactElement {
  const { t, i18n } = useTranslation()
  const { customer, history, deliveredValue } = details
  const rate = deliveryRate(history.delivered, history.returned)

  return (
    <>
      <header className="flex flex-col gap-1">
        <div className="flex flex-wrap items-center gap-2">
          <h1 className="text-2xl font-bold">{customer.name ?? customer.phone}</h1>
          {customer.isBlocked && (
            <span className="rounded-full bg-destructive/10 px-2 py-0.5 text-xs font-medium text-destructive">
              {t('customer.blockedBadge')}
            </span>
          )}
        </div>
        <p className="text-muted-foreground">{customer.phone}</p>
        <RiskBadge risk={details.risk} />
      </header>

      <dl aria-label={t('customer.track')} className="grid grid-cols-3 gap-2 text-center text-sm">
        <Tile label={t('customer.orders')} value={String(history.previousOrders)} />
        <Tile label={t('customer.delivered')} value={String(history.delivered)} />
        <Tile label={t('customer.returned')} value={String(history.returned)} />
        <Tile label={t('customer.cancelled')} value={String(history.cancelled)} />
        <Tile
          label={t('customer.deliveryRate')}
          value={rate === null ? '—' : t('customer.percent', { value: rate })}
        />
        <Tile
          label={t('customer.deliveredValue')}
          value={formatMoney(deliveredValue, i18n.language)}
        />
      </dl>

      <BlockPanel key={`${customer.id}-${String(customer.isBlocked)}`} customer={customer} />
      <DetailsForm key={customer.id} customer={customer} />
      <CustomerOrders customerId={customer.id} />
    </>
  )
}

/** Client errors are i18n keys; server messages (ProblemDetails) are shown as they come. */
function useMessage(): (message: string) => string {
  const { t, i18n } = useTranslation()
  return (message) => (i18n.exists(message) ? t(message) : message)
}

function Tile({ label, value }: { label: string; value: string }): ReactElement {
  return (
    <div className="rounded-lg border p-2">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-lg font-semibold">{value}</dd>
    </div>
  )
}

const reasonSchema = z.object({ reason: z.string().trim().max(200, 'order.tooLong') })

/** Blocking warns whoever takes this buyer's next order; it does not refuse the order. */
function BlockPanel({ customer }: { customer: CustomerDto }): ReactElement {
  const { t } = useTranslation()
  const showMessage = useMessage()
  const queryClient = useQueryClient()
  const form = useForm({ resolver: zodResolver(reasonSchema), defaultValues: { reason: '' } })
  const change = useMutation({
    mutationFn: (reason: string | null) =>
      reason === null
        ? deleteApiCustomersIdBlock(customer.id)
        : postApiCustomersIdBlock(customer.id, { reason: reason || null }),
    onSuccess: (updated) => {
      afterCustomerChange(queryClient, updated)
    },
    onError: (error) => {
      applyProblem(error, form.setError, ['reason'])
    },
  })
  const rootError = form.formState.errors.root?.message

  if (customer.isBlocked) {
    return (
      <div role="note" className="flex flex-col gap-2 rounded-lg border border-destructive/40 p-3">
        <p className="text-sm">
          {customer.blockedReason
            ? t('customer.blockedBecause', { reason: customer.blockedReason })
            : t('customer.blockedNoReason')}
        </p>
        <Button
          variant="outline"
          size="sm"
          className="self-start"
          disabled={change.isPending}
          onClick={() => {
            change.mutate(null)
          }}
        >
          {t('customer.unblock')}
        </Button>
        {rootError && (
          <p role="alert" className="text-sm text-destructive">
            {showMessage(rootError)}
          </p>
        )}
      </div>
    )
  }

  return (
    <form
      noValidate
      className="flex flex-col gap-2 rounded-lg border p-3"
      onSubmit={(event) =>
        void form.handleSubmit((values) => {
          change.mutate(values.reason)
        })(event)
      }
    >
      <FormField
        label={t('customer.blockReason')}
        placeholder={t('customer.blockReasonHint')}
        error={form.formState.errors.reason?.message}
        {...form.register('reason')}
      />
      <Button
        type="submit"
        variant="destructive"
        size="sm"
        className="self-start"
        disabled={change.isPending}
      >
        {t('customer.block')}
      </Button>
      {rootError && (
        <p role="alert" className="text-sm text-destructive">
          {showMessage(rootError)}
        </p>
      )}
    </form>
  )
}

const detailsSchema = z.object({
  name: z.string().trim().max(100, 'order.tooLong'),
  address: z.string().trim().max(300, 'order.tooLong'),
  notes: z.string().trim().max(1000, 'order.tooLong'),
})
type DetailsValues = z.infer<typeof detailsSchema>
const detailFields = ['name', 'address', 'notes'] as const

function DetailsForm({ customer }: { customer: CustomerDto }): ReactElement {
  const { t } = useTranslation()
  const showMessage = useMessage()
  const queryClient = useQueryClient()
  const notesId = useId()
  const form = useForm<DetailsValues>({
    resolver: zodResolver(detailsSchema),
    defaultValues: {
      name: customer.name ?? '',
      address: customer.address ?? '',
      notes: customer.notes ?? '',
    },
  })
  // PUT replaces all three fields, so the form always sends all three (empty = cleared).
  const save = useMutation({
    mutationFn: (values: DetailsValues) =>
      putApiCustomersId(customer.id, {
        name: values.name || null,
        address: values.address || null,
        notes: values.notes || null,
      }),
    onSuccess: (updated, values) => {
      afterCustomerChange(queryClient, updated)
      form.reset(values) // saved values become the new baseline (not dirty)
    },
    onError: (error) => {
      applyProblem(error, form.setError, detailFields)
    },
  })
  const { errors, isDirty } = form.formState
  const notesError = errors.notes?.message

  return (
    <form
      noValidate
      aria-label={t('customer.details')}
      className="flex flex-col gap-3"
      onSubmit={(event) =>
        void form.handleSubmit((values) => {
          save.mutate(values)
        })(event)
      }
    >
      <h2 className="font-semibold">{t('customer.details')}</h2>
      <FormField label={t('order.name')} error={errors.name?.message} {...form.register('name')} />
      <FormField
        label={t('order.address')}
        error={errors.address?.message}
        {...form.register('address')}
      />
      <div className="flex flex-col gap-1.5">
        <label htmlFor={notesId} className="text-sm font-medium">
          {t('customer.notes')}
        </label>
        <textarea
          id={notesId}
          rows={3}
          aria-invalid={Boolean(notesError)}
          className="rounded-md border border-input bg-transparent px-3 py-2 text-base outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50 md:text-sm"
          {...form.register('notes')}
        />
        {notesError && (
          <p role="alert" className="text-sm text-destructive">
            {showMessage(notesError)}
          </p>
        )}
      </div>
      <div className="flex items-center gap-3">
        <Button type="submit" disabled={save.isPending || !isDirty}>
          {save.isPending ? t('order.saving') : t('customer.save')}
        </Button>
        {save.isSuccess && !isDirty && (
          <span role="status" className="text-sm text-green-800">
            {t('customer.saved')}
          </span>
        )}
      </div>
      {errors.root?.message && (
        <p role="alert" className="text-sm text-destructive">
          {showMessage(errors.root.message)}
        </p>
      )}
    </form>
  )
}

function CustomerOrders({ customerId }: { customerId: string }): ReactElement {
  const { t } = useTranslation()
  const list = useInfiniteQuery(orderListQuery({ CustomerId: customerId }))
  const orders = list.data?.pages.flatMap((page) => page.items) ?? []

  return (
    <section className="flex flex-col gap-2">
      <h2 className="font-semibold">{t('customer.orderList')}</h2>
      {list.isPending && <p>{t('common.loading')}</p>}
      {list.isError && (
        <p role="alert" className="text-destructive">
          {t('errors.unexpected')}
        </p>
      )}
      {list.isSuccess && orders.length === 0 && (
        <p className="text-muted-foreground">{t('customer.noOrders')}</p>
      )}
      <ul aria-label={t('customer.orderList')} className="flex flex-col gap-2">
        {orders.map((order) => (
          <OrderRow key={order.id} order={order} customer="hidden" />
        ))}
      </ul>
      {list.hasNextPage && (
        <Button
          variant="outline"
          disabled={list.isFetchingNextPage}
          onClick={() => void list.fetchNextPage()}
        >
          {list.isFetchingNextPage ? t('orders.loadingMore') : t('orders.loadMore')}
        </Button>
      )}
    </section>
  )
}
