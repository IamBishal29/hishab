import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type SubmitEvent, type ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useSearchParams } from 'react-router'
import {
  OrderSource,
  OrderStatus,
  postApiOrdersBulkCancel,
  postApiOrdersBulkConfirm,
  type BulkActionResult,
  type BulkItemResult,
} from '@/api/generated/hishab'
import { ApiProblem } from '@/api/http'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { canEnterOrders } from '@/features/auth/permissions'
import { useSession } from '@/features/auth/useSession'
import { parseFilters, toApiParams, toSearchParams, type OrderFilters } from './orderFilters'
import { canCancel, canConfirm, invalidateOrders, orderListQuery } from './orderQueries'
import { OrderRow } from './OrderRow'

const selectClass = 'h-10 w-full rounded-md border border-input bg-transparent px-2 text-sm'

export function OrderListPage(): ReactElement {
  const { t } = useTranslation()
  const [searchParams, setSearchParams] = useSearchParams()
  const filters = parseFilters(searchParams)
  const update = (change: Partial<OrderFilters>) => {
    setSearchParams(toSearchParams({ ...filters, ...change }))
  }

  return (
    <section className="mx-auto flex max-w-2xl flex-col gap-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">{t('orders.title')}</h1>
        <Button asChild size="sm">
          <Link to="/orders/new">{t('orders.newOrder')}</Link>
        </Button>
      </div>
      <FilterBar
        filters={filters}
        onChange={update}
        onClear={() => {
          setSearchParams({})
        }}
      />
      {/* A new filter is a new list: the key resets the selection and earlier bulk results. */}
      <OrderResults key={toSearchParams(filters).toString()} filters={filters} />
    </section>
  )
}

interface FilterBarProps {
  filters: OrderFilters
  onChange: (change: Partial<OrderFilters>) => void
  onClear: () => void
}

function FilterBar({ filters, onChange, onClear }: FilterBarProps): ReactElement {
  const { t } = useTranslation()
  const toggleStatus = (status: OrderStatus) => {
    onChange({
      status: filters.status.includes(status)
        ? filters.status.filter((s) => s !== status)
        : [...filters.status, status],
    })
  }
  // Search applies on submit (Enter / search key on a phone), one request per search.
  // The key re-creates the input when the URL changes from outside (Back, a shared link).
  const submitSearch = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault()
    const value = new FormData(event.currentTarget).get('q')
    onChange({ q: typeof value === 'string' ? value.trim() : '' })
  }

  return (
    <div aria-label={t('orders.filters')} role="group" className="flex flex-col gap-3">
      <form key={filters.q} role="search" onSubmit={submitSearch}>
        <label className="flex flex-col gap-1.5 text-sm font-medium">
          {t('orders.search')}
          <Input
            name="q"
            type="search"
            defaultValue={filters.q}
            placeholder={t('orders.searchHint')}
          />
        </label>
      </form>

      <fieldset className="flex flex-wrap gap-1.5">
        <legend className="mb-1.5 text-sm font-medium">{t('orders.status')}</legend>
        {Object.values(OrderStatus).map((status) => (
          <Button
            key={status}
            type="button"
            size="xs"
            variant={filters.status.includes(status) ? 'default' : 'outline'}
            aria-pressed={filters.status.includes(status)}
            onClick={() => {
              toggleStatus(status)
            }}
          >
            {t(`status.${status}`)}
          </Button>
        ))}
      </fieldset>

      <div className="grid grid-cols-2 gap-2">
        <label className="flex flex-col gap-1.5 text-sm font-medium">
          {t('orders.from')}
          <Input
            type="date"
            value={filters.from ?? ''}
            onChange={(e) => {
              onChange({ from: e.target.value || null })
            }}
          />
        </label>
        <label className="flex flex-col gap-1.5 text-sm font-medium">
          {t('orders.to')}
          <Input
            type="date"
            value={filters.to ?? ''}
            onChange={(e) => {
              onChange({ to: e.target.value || null })
            }}
          />
        </label>
        <label className="flex flex-col gap-1.5 text-sm font-medium">
          {t('orders.area')}
          <select
            className={selectClass}
            value={filters.area ?? ''}
            onChange={(e) => {
              onChange({ area: e.target.value === '' ? null : (e.target.value as 'inside') })
            }}
          >
            <option value="">{t('orders.anyArea')}</option>
            <option value="inside">{t('orders.inside')}</option>
            <option value="outside">{t('orders.outside')}</option>
          </select>
        </label>
        <label className="flex flex-col gap-1.5 text-sm font-medium">
          {t('orders.source')}
          <select
            className={selectClass}
            value={filters.source ?? ''}
            onChange={(e) => {
              onChange({
                source: e.target.value === '' ? null : (e.target.value as OrderSource),
              })
            }}
          >
            <option value="">{t('orders.anySource')}</option>
            {Object.values(OrderSource).map((source) => (
              <option key={source} value={source}>
                {t(`order.sources.${source}`)}
              </option>
            ))}
          </select>
        </label>
      </div>
      <Button type="button" variant="link" size="sm" className="self-start" onClick={onClear}>
        {t('orders.clear')}
      </Button>
    </div>
  )
}

type BulkAction = 'confirm' | 'cancel'

function OrderResults({ filters }: { filters: OrderFilters }): ReactElement {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const { data: session } = useSession()
  const canAct = canEnterOrders(session?.role)
  const list = useInfiniteQuery(orderListQuery(toApiParams(filters)))
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set())
  const [results, setResults] = useState<ReadonlyMap<string, BulkItemResult>>(new Map())

  const bulk = useMutation({
    mutationFn: ({ action, orderIds }: { action: BulkAction; orderIds: string[] }) =>
      action === 'confirm'
        ? postApiOrdersBulkConfirm({ orderIds })
        : postApiOrdersBulkCancel({ orderIds }),
    onSuccess: (result: BulkActionResult) => {
      setResults(new Map(result.results.map((r) => [r.orderId, r])))
      // Keep failed orders selected so the packer can see and retry them.
      setSelected(new Set(result.results.filter((r) => !r.succeeded).map((r) => r.orderId)))
    },
    onSettled: (_data, _error, { orderIds }) => {
      invalidateOrders(queryClient, orderIds)
    },
  })

  if (list.isPending) return <p>{t('common.loading')}</p>
  if (list.isError) {
    return (
      <p role="alert" className="text-destructive">
        {t('errors.unexpected')}{' '}
        <Button variant="link" size="sm" onClick={() => void list.refetch()}>
          {t('common.retry')}
        </Button>
      </p>
    )
  }

  const orders = list.data.pages.flatMap((page) => page.items)
  if (orders.length === 0) return <p className="text-muted-foreground">{t('orders.empty')}</p>

  const selectedOrders = orders.filter((o) => selected.has(o.id))
  const toggle = (id: string) => {
    const next = new Set(selected)
    if (!next.delete(id)) next.add(id)
    setSelected(next)
  }
  const allSelected = orders.every((o) => selected.has(o.id))
  const run = (action: BulkAction) => {
    if (action === 'cancel' && !window.confirm(t('orders.cancelAsk', { count: selected.size })))
      return
    bulk.mutate({ action, orderIds: [...selected] })
  }

  return (
    <div className="flex flex-col gap-3">
      {canAct && (
        <div className="sticky top-0 z-10 flex flex-wrap items-center gap-2 border-b bg-background py-2">
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              className="size-5"
              checked={allSelected}
              onChange={() => {
                setSelected(allSelected ? new Set() : new Set(orders.map((o) => o.id)))
              }}
            />
            {t('orders.selectAll')}
          </label>
          {selected.size > 0 && (
            <>
              <span className="text-sm text-muted-foreground">
                {t('orders.selected', { count: selected.size })}
              </span>
              <Button
                size="sm"
                disabled={bulk.isPending || !selectedOrders.some((o) => canConfirm(o.status))}
                onClick={() => {
                  run('confirm')
                }}
              >
                {bulk.isPending ? t('orders.working') : t('orders.confirm')}
              </Button>
              <Button
                size="sm"
                variant="outline"
                disabled={bulk.isPending || !selectedOrders.some((o) => canCancel(o.status))}
                onClick={() => {
                  run('cancel')
                }}
              >
                {t('orders.cancel')}
              </Button>
            </>
          )}
        </div>
      )}
      <div aria-live="polite">
        {bulk.data && (
          <p role="status" className="text-sm">
            {t('orders.bulkDone', { succeeded: bulk.data.succeeded, failed: bulk.data.failed })}
          </p>
        )}
        {bulk.error && (
          <p role="alert" className="text-sm text-destructive">
            {bulk.error instanceof ApiProblem ? bulk.error.message : t('errors.network')}
          </p>
        )}
      </div>
      <ul className="flex flex-col gap-2">
        {orders.map((order) => (
          <OrderRow
            key={order.id}
            order={order}
            selection={
              canAct
                ? {
                    selected: selected.has(order.id),
                    onToggle: () => {
                      toggle(order.id)
                    },
                  }
                : undefined
            }
            result={results.get(order.id)}
            customer={canAct ? 'link' : 'text'}
          />
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
    </div>
  )
}
