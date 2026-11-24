import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useId, useState, type ReactElement } from 'react'
import { useFieldArray, useForm, useWatch, type Control } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import { z } from 'zod'
import { CourierProvider, postApiMoneyPayouts, useGetApiCouriers } from '@/api/generated/hishab'
import { FormField } from '@/components/FormField'
import { Button } from '@/components/ui/button'
import { canSeeProfit } from '@/features/auth/permissions'
import { useSession } from '@/features/auth/useSession'
import { useBookingChoice } from '@/features/couriers/courierQueries'
import { todayInDhaka } from '@/lib/dates'
import { formatMoney } from '@/lib/money'
import { applyProblem } from '@/lib/problemErrors'
import { afterPayoutChange } from './moneyQueries'
import { MoneyNotAllowed } from './MoneyParts'
import { parseStatement, type StatementRow } from './statementPaste'

// Same rule as the API's Money: at most 2 decimals, rejected rather than rounded.
const amount = z
  .string()
  .trim()
  .regex(/^\d+(\.\d{1,2})?$/, 'money.amountInvalid')
const schema = z.object({
  courier: z.enum(CourierProvider),
  reference: z.string().trim().min(1, 'money.required').max(100),
  paidOn: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, 'money.required'),
  netAmount: amount,
  note: z.string().trim().max(500),
  lines: z
    .array(
      z.object({
        consignmentId: z.string().trim().min(1, 'money.required'),
        collected: amount,
        deliveryFee: amount,
        codFee: amount,
      }),
    )
    .min(1, 'money.noLines')
    .max(500, 'money.tooManyLines'),
})
type PayoutForm = z.infer<typeof schema>
const fields = ['courier', 'reference', 'paidOn', 'netAmount', 'note', 'lines'] as const

type LineField = keyof PayoutForm['lines'][number]
/** React Hook Form types list paths as `lines.${number}.x`; build one from an index. */
const linePath = <F extends LineField>(index: number, field: F) =>
  `lines.${String(index)}.${field}` as `lines.${number}.${F}`

const emptyRow = (): StatementRow => ({
  consignmentId: '',
  collected: '',
  deliveryFee: '0',
  codFee: '0',
})

export function RecordPayoutPage(): ReactElement {
  const { t } = useTranslation()
  const { data: session } = useSession()
  const couriers = useGetApiCouriers()
  const choice = useBookingChoice()

  if (!canSeeProfit(session?.role)) return <MoneyNotAllowed />
  return (
    <section className="mx-auto flex max-w-2xl flex-col gap-4">
      <Link to="/money" className="text-sm underline-offset-4 hover:underline">
        ← {t('money.back')}
      </Link>
      <h1 className="text-2xl font-bold">{t('money.recordPayout')}</h1>
      {(couriers.isPending || choice.isPending) && <p>{t('common.loading')}</p>}
      {couriers.data && !choice.isPending && (
        <PayoutFormView
          providers={couriers.data.map((c) => c.provider)}
          initialCourier={choice.preferred ?? couriers.data[0]?.provider ?? CourierProvider.Fake}
        />
      )}
    </section>
  )
}

function PayoutFormView({
  providers,
  initialCourier,
}: {
  providers: CourierProvider[]
  initialCourier: CourierProvider
}): ReactElement {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const ids = { courier: useId(), note: useId(), paste: useId() }
  const form = useForm<PayoutForm>({
    resolver: zodResolver(schema),
    defaultValues: {
      courier: initialCourier,
      reference: '',
      paidOn: todayInDhaka(),
      netAmount: '',
      note: '',
      lines: [emptyRow()],
    },
  })
  const lines = useFieldArray({ control: form.control, name: 'lines' })
  const errors = form.formState.errors
  const show = (key?: string) => (key && i18n.exists(key) ? t(key) : key)

  const save = useMutation({
    mutationFn: (v: PayoutForm) =>
      postApiMoneyPayouts({
        courier: v.courier,
        reference: v.reference,
        paidOn: v.paidOn,
        netAmount: Number(v.netAmount),
        note: v.note === '' ? null : v.note,
        lines: v.lines.map((l) => ({
          consignmentId: l.consignmentId,
          collected: Number(l.collected),
          deliveryFee: Number(l.deliveryFee),
          codFee: Number(l.codFee),
        })),
      }),
    onSuccess: (saved) => {
      afterPayoutChange(queryClient, saved)
      void navigate(`/money/payouts/${saved.payout.id}`)
    },
    onError: (error) => {
      applyProblem(error, form.setError, fields)
    },
  })

  return (
    <form
      noValidate
      className="flex flex-col gap-4"
      onSubmit={(e) =>
        void form.handleSubmit((v) => {
          save.mutate(v)
        })(e)
      }
    >
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="flex flex-col gap-1.5">
          <label htmlFor={ids.courier} className="text-sm font-medium">
            {t('money.courier')}
          </label>
          <select
            id={ids.courier}
            className="h-10 rounded-md border border-input bg-transparent px-2 text-sm"
            {...form.register('courier')}
          >
            {providers.map((p) => (
              <option key={p} value={p}>
                {t(`couriers.names.${p}`)}
              </option>
            ))}
          </select>
        </div>
        <FormField
          label={t('money.reference')}
          error={errors.reference?.message}
          {...form.register('reference')}
        />
        <FormField
          label={t('money.paidOn')}
          type="date"
          error={errors.paidOn?.message}
          {...form.register('paidOn')}
        />
        <FormField
          label={t('money.netAmount')}
          inputMode="decimal"
          error={errors.netAmount?.message}
          {...form.register('netAmount')}
        />
      </div>
      <FormField label={t('money.note')} error={errors.note?.message} {...form.register('note')} />

      <PasteBox
        id={ids.paste}
        onRows={(rows) => {
          // A single untouched row is a placeholder: replace it rather than keep an empty line.
          const current = form.getValues('lines')
          const onlyPlaceholder = current.length === 1 && current[0]?.consignmentId === ''
          if (onlyPlaceholder) lines.replace(rows)
          else lines.append(rows)
        }}
      />

      <fieldset className="flex flex-col gap-3">
        <legend className="mb-1 font-semibold">{t('money.lines')}</legend>
        {lines.fields.map((field, index) => {
          const lineErrors = errors.lines?.[index]
          return (
            <div
              key={field.id}
              role="group"
              aria-label={t('money.parcelN', { n: index + 1 })}
              className="grid grid-cols-2 gap-2 rounded-lg border p-3 sm:grid-cols-4"
            >
              <FormField
                label={t('money.consignment')}
                error={lineErrors?.consignmentId?.message}
                {...form.register(linePath(index, 'consignmentId'))}
              />
              <FormField
                label={t('money.collected')}
                inputMode="decimal"
                error={lineErrors?.collected?.message}
                {...form.register(linePath(index, 'collected'))}
              />
              <FormField
                label={t('money.deliveryFee')}
                inputMode="decimal"
                error={lineErrors?.deliveryFee?.message}
                {...form.register(linePath(index, 'deliveryFee'))}
              />
              <FormField
                label={t('money.codFee')}
                inputMode="decimal"
                error={lineErrors?.codFee?.message}
                {...form.register(linePath(index, 'codFee'))}
              />
              {lines.fields.length > 1 && (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="col-span-2 justify-self-start sm:col-span-4"
                  onClick={() => {
                    lines.remove(index)
                  }}
                >
                  {t('money.removeParcel', { n: index + 1 })}
                </Button>
              )}
            </div>
          )
        })}
        {errors.lines?.message && (
          <p role="alert" className="text-sm text-destructive">
            {show(errors.lines.message)}
          </p>
        )}
        <Button
          type="button"
          variant="outline"
          className="self-start"
          onClick={() => {
            lines.append(emptyRow())
          }}
        >
          {t('money.addParcel')}
        </Button>
      </fieldset>

      <Totals control={form.control} />

      {errors.root?.message && (
        <p role="alert" className="text-sm text-destructive">
          {show(errors.root.message)}
        </p>
      )}
      <Button type="submit" disabled={save.isPending}>
        {save.isPending ? t('money.saving') : t('money.save')}
      </Button>
      <p className="text-xs text-muted-foreground">{t('money.saveHint')}</p>
    </form>
  )
}

/** Paste rows copied from the courier's statement (Excel, Sheets or CSV). */
function PasteBox({
  id,
  onRows,
}: {
  id: string
  onRows: (rows: StatementRow[]) => void
}): ReactElement {
  const { t } = useTranslation()
  const [text, setText] = useState('')
  const [result, setResult] = useState<string | null>(null)

  return (
    <div className="flex flex-col gap-1.5 rounded-lg border border-dashed p-3">
      <label htmlFor={id} className="text-sm font-medium">
        {t('money.pasteLabel')}
      </label>
      <p className="text-xs text-muted-foreground">{t('money.pasteHint')}</p>
      <textarea
        id={id}
        rows={3}
        value={text}
        onChange={(e) => {
          setText(e.target.value)
        }}
        className="rounded-md border border-input bg-transparent px-3 py-2 font-mono text-sm outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50"
      />
      <Button
        type="button"
        variant="outline"
        size="sm"
        className="self-start"
        disabled={text.trim() === ''}
        onClick={() => {
          const { rows, skipped } = parseStatement(text)
          if (rows.length > 0) onRows(rows)
          setText(skipped.length > 0 ? text : '')
          setResult(
            [
              t('money.pasted', { count: rows.length }),
              skipped.length > 0 ? t('money.pasteSkipped', { lines: skipped.join(', ') }) : '',
            ]
              .filter(Boolean)
              .join(' '),
          )
        }}
      >
        {t('money.addPasted')}
      </Button>
      {result && (
        <p role="status" className="text-sm">
          {result}
        </p>
      )}
    </div>
  )
}

/** Live check against the bank transfer. Display only: the API computes the real figures. */
function Totals({ control }: { control: Control<PayoutForm> }): ReactElement {
  const { t, i18n } = useTranslation()
  const [lines, netAmount] = useWatch({ control, name: ['lines', 'netAmount'] })
  const num = (s: string) => (Number.isFinite(Number(s)) ? Number(s) : 0)
  const statementNet = lines.reduce(
    (sum, l) => sum + num(l.collected) - num(l.deliveryFee) - num(l.codFee),
    0,
  )
  const difference = Math.round((num(netAmount) - statementNet) * 100) / 100
  const money = (v: number) => formatMoney(v, i18n.language)

  return (
    <dl
      aria-label={t('money.check')}
      className="grid grid-cols-2 gap-1 rounded-lg bg-muted p-3 text-sm"
    >
      <dt>{t('money.statementNet', { count: lines.length })}</dt>
      <dd className="text-right">{money(statementNet)}</dd>
      <dt>{t('money.difference')}</dt>
      <dd className={`text-right ${difference !== 0 ? 'text-destructive' : ''}`}>
        {money(difference)}
      </dd>
    </dl>
  )
}
