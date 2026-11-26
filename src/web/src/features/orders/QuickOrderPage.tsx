import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useId, useRef, useState, type ReactElement, type ReactNode } from 'react'
import { useFieldArray, useForm, useWatch, type UseFormReturn } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import {
  OrderSource,
  postApiOrders,
  useGetApiShop,
  type CreateOrderRequest,
  type CustomerLookupResponse,
  type OrderDraft,
  type ShopResponse,
} from '@/api/generated/hishab'
import { ApiProblem } from '@/api/http'
import { FormField } from '@/components/FormField'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { formatMoney } from '@/lib/money'
import { bdMobilePattern } from '@/lib/phone'
import { applyProblem } from '@/lib/problemErrors'
import { ChatDraftPanel, SuggestedBadge } from './ChatDraftPanel'
import {
  draftSuggestions,
  isSuggested,
  suggestibleFields,
  suggestionsToApply,
  type SuggestibleField,
  type Suggestions,
} from './chatDraft'
import { useIdempotencyKey } from './idempotencyKey'
import { ProductPicker, type PickedVariant } from './ProductPicker'
import { useCustomerLookup } from './useCustomerLookup'
import { RiskBadge } from '@/features/customers/RiskBadge'

const amount = z
  .number({ error: 'order.amountInvalid' })
  .min(0, 'order.amountInvalid')
  .max(5000, 'order.amountInvalid')
  .multipleOf(0.01, 'order.amountInvalid')

const orderSchema = z.object({
  phone: z.string().trim().regex(bdMobilePattern, 'login.phoneInvalid'),
  customerName: z.string().trim().max(100, 'order.tooLong'),
  deliveryAddress: z.string().trim().max(300, 'order.tooLong'),
  area: z.enum(['inside', 'outside']),
  source: z.enum(OrderSource),
  lines: z
    .array(
      z.object({
        variantId: z.string(),
        label: z.string(),
        unitPrice: z.number(),
        quantity: z
          .number({ error: 'order.quantityInvalid' })
          .int('order.quantityInvalid')
          .min(1, 'order.quantityInvalid')
          .max(1000, 'order.quantityInvalid'),
      }),
    )
    .min(1, 'order.linesRequired'),
  deliveryCharge: amount.nullable(),
  advancePaid: z
    .number({ error: 'order.amountInvalid' })
    .min(0, 'order.amountInvalid')
    .multipleOf(0.01, 'order.amountInvalid'),
  note: z.string().trim().max(500, 'order.tooLong'),
})

type OrderForm = z.infer<typeof orderSchema>

const fields = [
  'phone',
  'customerName',
  'deliveryAddress',
  'source',
  'lines',
  'deliveryCharge',
  'advancePaid',
  'note',
] as const

const emptyOrder = (source: OrderSource = OrderSource.Facebook): OrderForm => ({
  phone: '',
  customerName: '',
  deliveryAddress: '',
  area: 'inside',
  source,
  lines: [],
  deliveryCharge: null,
  advancePaid: 0,
  note: '',
})

/** React Hook Form types list paths as `lines.${number}`; build one from an index. */
const quantityPath = (index: number) =>
  `lines.${String(index)}.quantity` as `lines.${number}.quantity`
const emptyLine = { variantId: '', label: '', unitPrice: 0, quantity: 0 }

const optionalNumber = (value: unknown) => (value === '' || value === null ? null : Number(value))

function toRequest(values: OrderForm): CreateOrderRequest {
  return {
    phone: values.phone,
    customerName: values.customerName || null,
    deliveryAddress: values.deliveryAddress || null,
    isInsideDhaka: values.area === 'inside',
    source: values.source,
    lines: values.lines.map((l) => ({ variantId: l.variantId, quantity: l.quantity })),
    deliveryCharge: values.deliveryCharge,
    advancePaid: values.advancePaid,
    note: values.note || null,
  }
}

/** The 20-second order: phone first, the customer fills in, pick products, save. */
export function QuickOrderPage(): ReactElement {
  const { t } = useTranslation()
  const shop = useGetApiShop()

  if (shop.isPending) return <p className="p-4">{t('common.loading')}</p>
  if (shop.isError) return <p className="p-4 text-destructive">{t('errors.unexpected')}</p>
  return <QuickOrderForm shop={shop.data} />
}

function QuickOrderForm({ shop }: { shop: ShopResponse }): ReactElement {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const idempotencyKey = useIdempotencyKey()
  const [savedNumber, setSavedNumber] = useState<number | null>(null)
  const [suggested, setSuggested] = useState<Suggestions>({})
  /** Quantity the assistant suggested per variant; the mark stays while the quantity does. */
  const [suggestedLines, setSuggestedLines] = useState<Record<string, number>>({})
  const [resets, setResets] = useState(0) // a new order starts with an empty paste box

  const form = useForm<OrderForm>({
    resolver: zodResolver(orderSchema),
    defaultValues: emptyOrder(),
  })
  const lines = useFieldArray({ control: form.control, name: 'lines', keyName: 'key' })
  const lookup = useCustomerLookup(useWatch({ control: form.control, name: 'phone' }))
  useAutoFill(form, lookup.data)

  // After a save the form re-renders empty; then put the cursor back on the phone for the next order.
  useEffect(() => {
    if (savedNumber !== null) form.setFocus('phone')
  }, [savedNumber, form])

  const create = useMutation({
    mutationFn: ({ data, key }: { data: CreateOrderRequest; key: string }) =>
      postApiOrders(data, { headers: { 'Idempotency-Key': key } }),
  })

  const watched = useWatch({ control: form.control, name: suggestibleFields })
  // Read during render so React Hook Form tracks it: a field the seller typed in is dirty.
  // Auto-fill and suggestions use setValue without shouldDirty, so they stay replaceable.
  const { dirtyFields } = form.formState
  const mark = (field: SuggestibleField) =>
    isSuggested(suggested, field, watched[suggestibleFields.indexOf(field)] ?? '') ? (
      <SuggestedBadge />
    ) : null

  const applyDraft = (draft: OrderDraft) => {
    const current = form.getValues()
    const apply = suggestionsToApply(
      draftSuggestions(draft),
      current,
      (field) => dirtyFields[field] === true,
      suggested,
    )
    for (const field of suggestibleFields) {
      const value = apply[field]
      if (value === undefined) continue
      if (field === 'area') form.setValue('area', value === 'outside' ? 'outside' : 'inside')
      else form.setValue(field, value)
    }
    setSuggested((previous) => ({ ...previous, ...apply }))

    // Matched items join the list; one the seller already added keeps its quantity.
    const added: Record<string, number> = {}
    for (const line of draft.lines) {
      if (line.variantId === null || line.label === null || line.unitPrice === null) continue
      if (current.lines.some((l) => l.variantId === line.variantId)) continue
      lines.append({
        variantId: line.variantId,
        label: line.label,
        unitPrice: line.unitPrice,
        quantity: line.quantity,
      })
      added[line.variantId] = line.quantity
    }
    setSuggestedLines((previous) => ({ ...previous, ...added }))
    form.clearErrors()
  }

  const addLine = (variant: PickedVariant) => {
    const current = form.getValues('lines')
    const index = current.findIndex((l) => l.variantId === variant.variantId)
    const existing = current[index]
    if (existing) {
      lines.update(index, { ...existing, quantity: (existing.quantity || 0) + 1 }) // same product again: one more
    } else {
      lines.append({ ...variant, quantity: 1 })
    }
    form.clearErrors('lines')
  }

  const save = form.handleSubmit(async (values) => {
    setSavedNumber(null)
    try {
      // Same key on every retry of this attempt: a double tap or a retry after a lost
      // response returns the first order instead of creating another.
      const details = await create.mutateAsync({
        data: toRequest(values),
        key: idempotencyKey.current(),
      })
      idempotencyKey.settle()
      setSavedNumber(details.order.number)
      form.reset(emptyOrder(values.source)) // keep the channel: a moderator usually works one inbox
      setSuggested({})
      setSuggestedLines({})
      setResets((n) => n + 1)
      await queryClient.invalidateQueries({ queryKey: ['customers', 'lookup'] }) // their history changed
    } catch (error) {
      idempotencyKey.settle(error)
      if (error instanceof ApiProblem && error.status === 409) {
        // An earlier attempt with this key reached the server with different details.
        form.setError('root', { message: 'order.possibleDuplicate' })
      } else {
        applyProblem(error, form.setError, fields)
      }
    }
  })

  const errors = form.formState.errors
  const linesError = errors.lines?.message ?? errors.lines?.root?.message
  return (
    <section className="mx-auto flex w-full max-w-md flex-col gap-4">
      <h1 className="text-2xl font-bold">{t('order.title')}</h1>
      {savedNumber !== null && (
        <p role="status" className="rounded-md bg-green-50 p-3 text-sm text-green-900">
          {t('order.saved', { number: savedNumber })}
        </p>
      )}
      {errors.root?.message && (
        <p role="alert" className="text-sm text-destructive">
          {t(errors.root.message, { defaultValue: errors.root.message })}
        </p>
      )}
      <ChatDraftPanel key={resets} onDraft={applyDraft} />
      <form onSubmit={(e) => void save(e)} noValidate className="flex flex-col gap-4">
        <FormField
          label={t('order.phone')}
          badge={mark('phone')}
          type="tel"
          inputMode="tel"
          autoComplete="off"
          autoFocus
          error={errors.phone?.message}
          {...form.register('phone')}
        />
        <CustomerPanel lookup={lookup} />
        <FormField
          label={t('order.name')}
          badge={mark('customerName')}
          error={errors.customerName?.message}
          {...form.register('customerName')}
        />
        <FormField
          label={t('order.address')}
          badge={mark('deliveryAddress')}
          error={errors.deliveryAddress?.message}
          {...form.register('deliveryAddress')}
        />
        <AreaAndSource form={form} areaBadge={mark('area')} />

        <ProductPicker onPick={addLine} />
        <fieldset className="flex flex-col gap-2">
          <legend className="text-sm font-medium">{t('order.items')}</legend>
          {lines.fields.map((line, index) => (
            <LineRow
              key={line.key}
              form={form}
              index={index}
              suggestedQuantity={suggestedLines[line.variantId]}
              onRemove={() => {
                lines.remove(index)
              }}
            />
          ))}
          {linesError && (
            <p role="alert" className="text-sm text-destructive">
              {t(linesError, { defaultValue: linesError })}
            </p>
          )}
        </fieldset>

        <FormField
          label={t('order.deliveryCharge')}
          type="number"
          inputMode="decimal"
          step="0.01"
          placeholder={t('order.deliveryChargeHint')}
          error={errors.deliveryCharge?.message}
          {...form.register('deliveryCharge', { setValueAs: optionalNumber })}
        />
        <FormField
          label={t('order.advance')}
          type="number"
          inputMode="decimal"
          step="0.01"
          error={errors.advancePaid?.message}
          {...form.register('advancePaid', { setValueAs: (v: unknown) => optionalNumber(v) ?? 0 })}
        />
        <FormField
          label={t('order.note')}
          badge={mark('note')}
          error={errors.note?.message}
          {...form.register('note')}
        />

        <Totals form={form} shop={shop} />
        <Button type="submit" disabled={create.isPending}>
          {create.isPending ? t('order.saving') : t('order.save')}
        </Button>
      </form>
    </section>
  )
}

/**
 * Copies the known customer's name and address into the form, but only into fields that are
 * empty or still hold what we filled in before. What the moderator typed is never overwritten;
 * switching to another number replaces the previous auto-fill.
 */
function useAutoFill(
  form: UseFormReturn<OrderForm>,
  found: CustomerLookupResponse | null | undefined,
) {
  const filled = useRef<Partial<Record<'customerName' | 'deliveryAddress', string>>>({})
  useEffect(() => {
    if (found === undefined) return // still looking, or the number is incomplete
    const next = {
      customerName: found?.customer.name ?? '',
      deliveryAddress: found?.customer.address ?? '',
    }
    for (const field of ['customerName', 'deliveryAddress'] as const) {
      const current = form.getValues(field)
      if (current === '' || current === filled.current[field]) {
        form.setValue(field, next[field])
        filled.current[field] = next[field]
      }
    }
  }, [found, form])
}

function CustomerPanel({
  lookup,
}: {
  lookup: ReturnType<typeof useCustomerLookup>
}): ReactElement | null {
  const { t } = useTranslation()
  if (lookup.phone === null) return null
  if (lookup.isFetching && lookup.data === undefined) {
    return <p className="text-sm text-muted-foreground">{t('order.checking')}</p>
  }
  if (lookup.data === null)
    return <p className="text-sm text-muted-foreground">{t('order.newCustomer')}</p>
  if (!lookup.data) return null

  const { customer, history } = lookup.data
  return (
    <div className="flex flex-col gap-2">
      {customer.isBlocked && (
        <p
          role="alert"
          className="rounded-md border border-destructive p-3 text-sm text-destructive"
        >
          {customer.blockedReason
            ? t('order.blocked', { reason: customer.blockedReason })
            : t('order.blockedNoReason')}
        </p>
      )}
      <RiskBadge risk={lookup.data.risk} />
      <p className="text-sm text-muted-foreground">
        {t('order.history', {
          previous: history.previousOrders,
          delivered: history.delivered,
          returned: history.returned,
          cancelled: history.cancelled,
        })}
      </p>
    </div>
  )
}

function AreaAndSource({
  form,
  areaBadge,
}: {
  form: UseFormReturn<OrderForm>
  areaBadge: ReactNode
}): ReactElement {
  const { t } = useTranslation()
  const sourceId = useId()
  return (
    <div className="flex flex-col gap-4">
      <fieldset className="flex flex-col gap-1.5">
        <legend className="text-sm font-medium">
          {t('order.area')}
          {areaBadge}
        </legend>
        <div className="flex gap-4">
          <label className="flex items-center gap-2 text-sm">
            <input type="radio" value="inside" {...form.register('area')} />
            {t('order.insideDhaka')}
          </label>
          <label className="flex items-center gap-2 text-sm">
            <input type="radio" value="outside" {...form.register('area')} />
            {t('order.outsideDhaka')}
          </label>
        </div>
      </fieldset>
      <div className="flex flex-col gap-1.5">
        <label htmlFor={sourceId} className="text-sm font-medium">
          {t('order.source')}
        </label>
        <select
          id={sourceId}
          className="h-10 rounded-md border border-input bg-transparent px-3 text-base md:text-sm"
          {...form.register('source')}
        >
          {Object.values(OrderSource).map((source) => (
            <option key={source} value={source}>
              {t(`order.sources.${source}`)}
            </option>
          ))}
        </select>
      </div>
    </div>
  )
}

function LineRow({
  form,
  index,
  suggestedQuantity,
  onRemove,
}: {
  form: UseFormReturn<OrderForm>
  index: number
  /** Set when the assistant added this line; the mark goes once the quantity is changed. */
  suggestedQuantity: number | undefined
  onRemove: () => void
}): ReactElement {
  const { t, i18n } = useTranslation()
  const quantityId = useId()
  const line = useWatch({ control: form.control, name: 'lines' })[index] ?? emptyLine
  const error = form.formState.errors.lines?.[index]
  const message = error?.quantity?.message ?? error?.variantId?.message
  return (
    <div className="flex flex-col gap-1 rounded-md border p-2">
      <div className="flex items-center justify-between gap-2 text-sm">
        <span>
          {line.label}
          {suggestedQuantity !== undefined && suggestedQuantity === line.quantity && (
            <SuggestedBadge />
          )}
        </span>
        <span className="tabular-nums">
          {formatMoney(line.unitPrice * (line.quantity || 0), i18n.language)}
        </span>
      </div>
      <div className="flex items-center gap-2">
        <label htmlFor={quantityId} className="sr-only">
          {t('order.quantity', { product: line.label })}
        </label>
        <Input
          id={quantityId}
          type="number"
          inputMode="numeric"
          min={1}
          className="w-20"
          aria-invalid={Boolean(message)}
          {...form.register(quantityPath(index), { valueAsNumber: true })}
        />
        <span className="text-sm text-muted-foreground">
          × {formatMoney(line.unitPrice, i18n.language)}
        </span>
        <Button type="button" variant="ghost" size="sm" className="ml-auto" onClick={onRemove}>
          {t('order.remove', { product: line.label })}
        </Button>
      </div>
      {message && (
        <p role="alert" className="text-sm text-destructive">
          {t(message, { defaultValue: message })}
        </p>
      )}
    </div>
  )
}

/** A live preview. The server recomputes everything from the catalogue and shop settings on save. */
function Totals({
  form,
  shop,
}: {
  form: UseFormReturn<OrderForm>
  shop: ShopResponse
}): ReactElement {
  const { t, i18n } = useTranslation()
  const [lines, area, override, advance] = useWatch({
    control: form.control,
    name: ['lines', 'area', 'deliveryCharge', 'advancePaid'],
  })
  const subtotal = lines.reduce(
    (sum, l) => sum + l.unitPrice * (Number.isFinite(l.quantity) ? l.quantity : 0),
    0,
  )
  const shopRate =
    area === 'inside' ? shop.deliveryChargeInsideDhaka : shop.deliveryChargeOutsideDhaka
  const delivery = override ?? shopRate
  const total = subtotal + delivery
  const cod = total - (Number.isFinite(advance) ? advance : 0)
  const money = (value: number) => formatMoney(value, i18n.language)
  const rows: [string, string][] = [
    [t('order.subtotal'), money(subtotal)],
    [t('order.delivery'), money(delivery)],
    [t('order.total'), money(total)],
    [t('order.cod'), money(cod)],
  ]
  return (
    <dl
      aria-label={t('order.summary')}
      className="grid grid-cols-2 gap-x-4 gap-y-1 rounded-md bg-muted p-3 text-sm"
    >
      {rows.map(([label, value]) => (
        <div key={label} className="contents">
          <dt>{label}</dt>
          <dd className="text-right font-medium tabular-nums">{value}</dd>
        </div>
      ))}
    </dl>
  )
}
