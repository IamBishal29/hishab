import type { ReactElement } from 'react'
import { useTranslation } from 'react-i18next'

/** Shown instead of a money screen to staff without profit access (the API refuses them too). */
export function MoneyNotAllowed(): ReactElement {
  const { t } = useTranslation()
  return <p className="mx-auto max-w-2xl">{t('money.notAllowed')}</p>
}

export function Tile({
  label,
  value,
  warn = false,
}: {
  label: string
  value: string
  warn?: boolean
}): ReactElement {
  return (
    <div className="rounded-lg border p-2">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className={`font-semibold ${warn ? 'text-destructive' : ''}`}>{value}</dd>
    </div>
  )
}
