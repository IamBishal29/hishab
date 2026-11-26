import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useId, useState, type KeyboardEvent, type ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import { getApiProducts, getGetApiProductsQueryKey } from '@/api/generated/hishab'
import { Input } from '@/components/ui/input'
import { formatMoney } from '@/lib/money'
import { useDebouncedValue } from '@/lib/useDebouncedValue'

/** A catalogue variant as the order form shows it (price for the preview only: the API prices the order). */
export interface PickedVariant {
  variantId: string
  label: string
  unitPrice: number
}

/** Search the catalogue and pick variants. Enter adds the first match, so a keyboard user never leaves the field. */
export function ProductPicker({
  onPick,
}: {
  onPick: (variant: PickedVariant) => void
}): ReactElement {
  const { t, i18n } = useTranslation()
  const id = useId()
  const [search, setSearch] = useState('')
  const params = { Search: useDebouncedValue(search.trim()) || undefined, PageSize: 10 }
  const products = useQuery({
    queryKey: getGetApiProductsQueryKey(params),
    queryFn: ({ signal }) => getApiProducts(params, { signal }),
    placeholderData: keepPreviousData, // keep the old list while the next search loads: no flicker
  })

  const options: PickedVariant[] = (products.data?.items ?? []).flatMap((p) =>
    p.variants.map((v) => {
      const extra = [v.size, v.colour].filter(Boolean).join(' / ')
      return { variantId: v.id, label: extra ? `${p.name} · ${extra}` : p.name, unitPrice: v.price }
    }),
  )

  const pick = (option: PickedVariant) => {
    onPick(option)
    setSearch('')
  }

  const onKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key !== 'Enter') return
    e.preventDefault() // Enter here picks a product; it must not submit the order
    if (options[0]) pick(options[0])
  }

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-sm font-medium">
        {t('order.findProduct')}
      </label>
      <Input
        id={id}
        type="search"
        value={search}
        placeholder={t('order.findProductHint')}
        aria-describedby={`${id}-enter`}
        onChange={(e) => {
          setSearch(e.target.value)
        }}
        onKeyDown={onKeyDown}
      />
      <p id={`${id}-enter`} className="text-xs text-muted-foreground">
        {t('order.findProductEnter')}
      </p>
      <ul
        aria-label={t('order.productResults')}
        className="flex max-h-56 flex-col overflow-y-auto rounded-md border"
      >
        {options.length === 0 && (
          <li className="p-2 text-sm text-muted-foreground">
            {products.isPending ? t('common.loading') : t('order.noProducts')}
          </li>
        )}
        {options.map((option) => (
          <li key={option.variantId}>
            <button
              type="button"
              className="flex w-full items-center justify-between gap-2 px-3 py-2 text-left text-sm hover:bg-accent focus-visible:bg-accent focus-visible:outline-none"
              onClick={() => {
                pick(option)
              }}
            >
              <span>{option.label}</span>
              <span className="shrink-0 tabular-nums">
                {formatMoney(option.unitPrice, i18n.language)}
              </span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  )
}
