import { zodResolver } from '@hookform/resolvers/zod'
import { useQueryClient } from '@tanstack/react-query'
import type { ReactElement } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { z } from 'zod'
import {
  getGetApiShopQueryKey,
  usePutApiShop,
  useGetApiShop,
  type ShopResponse,
} from '@/api/generated/hishab'
import { FormField } from '@/components/FormField'
import { Button } from '@/components/ui/button'
import { applyProblem } from '@/lib/problemErrors'

const charge = z
  .number({ error: 'setup.chargeInvalid' })
  .min(0, 'setup.chargeInvalid')
  .max(5000, 'setup.chargeInvalid')
  .multipleOf(0.01, 'setup.chargeInvalid')

const setupSchema = z.object({
  name: z.string().trim().min(1, 'setup.nameRequired').max(100, 'setup.nameRequired'),
  deliveryChargeInsideDhaka: charge,
  deliveryChargeOutsideDhaka: charge,
})

type SetupForm = z.infer<typeof setupSchema>
const fields = ['name', 'deliveryChargeInsideDhaka', 'deliveryChargeOutsideDhaka'] as const

/** First-login setup: the shop name and delivery charges (prefilled with the API defaults). */
export function ShopSetupPage(): ReactElement {
  const { t } = useTranslation()
  const shop = useGetApiShop()

  if (shop.isPending) return <p className="p-4">{t('common.loading')}</p>
  if (shop.isError) return <p className="p-4 text-destructive">{t('errors.unexpected')}</p>

  return <SetupForm shop={shop.data} />
}

function SetupForm({ shop }: { shop: ShopResponse }): ReactElement {
  const { t } = useTranslation()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const update = usePutApiShop()

  const form = useForm<SetupForm>({
    resolver: zodResolver(setupSchema),
    defaultValues: {
      name: shop.name === 'My shop' ? '' : shop.name,
      deliveryChargeInsideDhaka: shop.deliveryChargeInsideDhaka,
      deliveryChargeOutsideDhaka: shop.deliveryChargeOutsideDhaka,
    },
  })

  const save = form.handleSubmit(async (values) => {
    try {
      // PUT replaces the profile: send the fields this screen does not edit unchanged.
      const saved = await update.mutateAsync({
        data: {
          ...values,
          logoUrl: shop.logoUrl,
          pickupAddress: shop.pickupAddress,
          defaultCourier: shop.defaultCourier,
        },
      })
      queryClient.setQueryData(getGetApiShopQueryKey(), saved)
      await navigate('/', { replace: true })
    } catch (error) {
      applyProblem(error, form.setError, fields)
    }
  })

  const errors = form.formState.errors
  return (
    <section className="mx-auto flex w-full max-w-sm flex-col gap-4">
      <header>
        <h1 className="text-2xl font-bold">{t('setup.title')}</h1>
        <p className="text-muted-foreground">{t('setup.subtitle')}</p>
      </header>
      {errors.root?.message && (
        <p role="alert" className="text-sm text-destructive">
          {t(errors.root.message, { defaultValue: errors.root.message })}
        </p>
      )}
      <form onSubmit={(e) => void save(e)} noValidate className="flex flex-col gap-4">
        <FormField
          label={t('setup.name')}
          error={errors.name?.message}
          {...form.register('name')}
        />
        <FormField
          label={t('setup.insideDhaka')}
          type="number"
          inputMode="decimal"
          step="0.01"
          error={errors.deliveryChargeInsideDhaka?.message}
          {...form.register('deliveryChargeInsideDhaka', { valueAsNumber: true })}
        />
        <FormField
          label={t('setup.outsideDhaka')}
          type="number"
          inputMode="decimal"
          step="0.01"
          error={errors.deliveryChargeOutsideDhaka?.message}
          {...form.register('deliveryChargeOutsideDhaka', { valueAsNumber: true })}
        />
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {t('setup.save')}
        </Button>
      </form>
    </section>
  )
}
