import { zodResolver } from '@hookform/resolvers/zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type ReactElement } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import {
  deleteApiCouriersProvider,
  getGetApiShopQueryKey,
  putApiCouriersProvider,
  putApiShop,
  useGetApiCouriers,
  useGetApiShop,
  type CourierConnection,
  type ShopResponse,
} from '@/api/generated/hishab'
import { FormField } from '@/components/FormField'
import { Button } from '@/components/ui/button'
import { canManageShop } from '@/features/auth/permissions'
import { useSession } from '@/features/auth/useSession'
import { formatDhakaDateTime } from '@/lib/dates'
import { applyProblem, problemMessage } from '@/lib/problemErrors'
import { afterCourierChange } from './courierQueries'

/** Courier connections: keys go in once and are never shown again (write-only). */
export function CourierSettingsPage(): ReactElement {
  const { t } = useTranslation()
  const { data: session } = useSession()
  const couriers = useGetApiCouriers()
  const shop = useGetApiShop()

  if (!canManageShop(session?.role)) {
    return <p className="mx-auto max-w-2xl">{t('couriers.notAllowed')}</p>
  }

  return (
    <section className="mx-auto flex max-w-2xl flex-col gap-4">
      <header>
        <h1 className="text-2xl font-bold">{t('couriers.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('couriers.intro')}</p>
      </header>
      {(couriers.isPending || shop.isPending) && <p>{t('common.loading')}</p>}
      {(couriers.isError || shop.isError) && (
        <p role="alert" className="text-destructive">
          {t('errors.unexpected')}
        </p>
      )}
      {couriers.data &&
        shop.data &&
        couriers.data.map((courier) => (
          <CourierCard key={courier.provider} courier={courier} shop={shop.data} />
        ))}
    </section>
  )
}

const keysSchema = z.object({
  credentials: z.record(z.string(), z.string().trim().min(1, 'couriers.required')),
})
type KeysForm = z.infer<typeof keysSchema>

function CourierCard({
  courier,
  shop,
}: {
  courier: CourierConnection
  shop: ShopResponse
}): ReactElement {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const name = t(`couriers.names.${courier.provider}`)
  const [editing, setEditing] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const isDefault = shop.defaultCourier === courier.provider

  const disconnect = useMutation({
    mutationFn: () => deleteApiCouriersProvider(courier.provider),
    onSuccess: () => {
      afterCourierChange(queryClient, {
        ...courier,
        isConnected: false,
        credentialHint: null,
        connectedAt: null,
      })
      setNotice(t('couriers.disconnected', { name }))
    },
  })
  const makeDefault = useMutation({
    // PUT replaces the profile: send the fields this screen does not edit unchanged.
    mutationFn: () =>
      putApiShop({
        name: shop.name,
        logoUrl: shop.logoUrl,
        pickupAddress: shop.pickupAddress,
        deliveryChargeInsideDhaka: shop.deliveryChargeInsideDhaka,
        deliveryChargeOutsideDhaka: shop.deliveryChargeOutsideDhaka,
        defaultCourier: courier.provider,
      }),
    onSuccess: (saved) => {
      queryClient.setQueryData(getGetApiShopQueryKey(), saved)
    },
  })
  const failure = disconnect.error ?? makeDefault.error

  const showForm = editing || !courier.isConnected
  return (
    <article aria-label={name} className="flex flex-col gap-3 rounded-lg border p-4">
      <div className="flex items-center justify-between gap-2">
        <h2 className="font-semibold">{name}</h2>
        {isDefault && (
          <span className="rounded-full bg-primary/10 px-2 py-0.5 text-xs">
            {t('couriers.default')}
          </span>
        )}
      </div>
      {courier.isConnected && courier.connectedAt ? (
        <p className="text-sm">
          {t('couriers.connectedAs', {
            hint: courier.credentialHint ?? '',
            date: formatDhakaDateTime(courier.connectedAt, i18n.language),
          })}
        </p>
      ) : (
        <p className="text-sm text-muted-foreground">{t('couriers.notConnected')}</p>
      )}
      {notice && (
        <p role="status" className="text-sm text-green-800">
          {notice}
        </p>
      )}

      {showForm ? (
        <KeysForm
          courier={courier}
          onDone={(updated) => {
            setEditing(false)
            setNotice(t('couriers.saved', { name }))
            afterCourierChange(queryClient, updated)
          }}
          onCancel={
            courier.isConnected
              ? () => {
                  setEditing(false)
                }
              : undefined
          }
        />
      ) : (
        <div className="flex flex-wrap gap-2">
          <Button
            size="sm"
            variant="outline"
            onClick={() => {
              setNotice(null)
              setEditing(true)
            }}
          >
            {t('couriers.replace')}
          </Button>
          {!isDefault && (
            <Button
              size="sm"
              variant="outline"
              disabled={makeDefault.isPending}
              onClick={() => {
                makeDefault.mutate()
              }}
            >
              {t('couriers.makeDefault')}
            </Button>
          )}
          <Button
            size="sm"
            variant="destructive"
            disabled={disconnect.isPending}
            onClick={() => {
              if (window.confirm(t('couriers.disconnectAsk', { name }))) disconnect.mutate()
            }}
          >
            {t('couriers.disconnect')}
          </Button>
        </div>
      )}
      {failure && (
        <p role="alert" className="text-sm text-destructive">
          {t(problemMessage(failure))}
        </p>
      )}
    </article>
  )
}

function KeysForm({
  courier,
  onDone,
  onCancel,
}: {
  courier: CourierConnection
  onDone: (updated: CourierConnection) => void
  onCancel: (() => void) | undefined
}): ReactElement {
  const { t } = useTranslation()
  const [reveal, setReveal] = useState(false)
  const form = useForm<KeysForm>({
    resolver: zodResolver(keysSchema),
    // Always empty: the API never returns keys, so there is nothing to prefill.
    defaultValues: {
      credentials: Object.fromEntries(courier.fields.map((f) => [f.name, ''])),
    },
  })

  const save = form.handleSubmit(async ({ credentials }) => {
    try {
      const updated = await putApiCouriersProvider(courier.provider, { credentials })
      form.reset() // drop the typed keys from memory as soon as the server has them
      onDone(updated)
    } catch (error) {
      // Field keys come back as `credentials.apiKey`; a bare `credentials` (rejected keys) goes to root.
      applyProblem(
        error,
        form.setError,
        courier.fields.map((f) => `credentials.${f.name}` as const),
      )
    }
  })

  const errors = form.formState.errors
  const formError = errors.root?.message
  return (
    <form noValidate onSubmit={(e) => void save(e)} className="flex flex-col gap-3">
      {courier.fields.map((field) => (
        <FormField
          key={field.name}
          label={field.label}
          type={reveal ? 'text' : 'password'}
          autoComplete="off"
          spellCheck={false}
          error={errors.credentials?.[field.name]?.message}
          {...form.register(`credentials.${field.name}`)}
        />
      ))}
      <label className="flex items-center gap-2 text-sm">
        <input
          type="checkbox"
          className="size-4"
          checked={reveal}
          onChange={() => {
            setReveal(!reveal)
          }}
        />
        {t('couriers.showKeys')}
      </label>
      <p className="text-xs text-muted-foreground">{t('couriers.writeOnly')}</p>
      {formError && (
        <p role="alert" className="text-sm text-destructive">
          {t(formError)}
        </p>
      )}
      <div className="flex gap-2">
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {form.formState.isSubmitting ? t('couriers.checking') : t('couriers.connect')}
        </Button>
        {onCancel && (
          <Button type="button" variant="ghost" onClick={onCancel}>
            {t('common.cancel')}
          </Button>
        )}
      </div>
    </form>
  )
}
