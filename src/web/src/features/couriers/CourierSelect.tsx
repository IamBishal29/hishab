import type { ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import type { CourierProvider } from '@/api/generated/hishab'
import { canManageShop } from '@/features/auth/permissions'
import { useSession } from '@/features/auth/useSession'

interface Props {
  connected: CourierProvider[]
  value: CourierProvider | null
  onChange: (courier: CourierProvider) => void
}

/** Pick one of the shop's connected couriers; explains (and links to settings) when there is none. */
export function CourierSelect({ connected, value, onChange }: Props): ReactElement {
  const { t } = useTranslation()
  const { data: session } = useSession()

  if (connected.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        {t('booking.noCourier')}{' '}
        {canManageShop(session?.role) && (
          <Link to="/settings/couriers" className="underline underline-offset-4">
            {t('booking.connectOne')}
          </Link>
        )}
      </p>
    )
  }

  return (
    <label className="flex flex-col gap-1.5 text-sm font-medium">
      {t('booking.courier')}
      <select
        className="h-10 rounded-md border border-input bg-transparent px-2 text-sm"
        value={value ?? ''}
        onChange={(e) => {
          onChange(e.target.value as CourierProvider)
        }}
      >
        {value === null && <option value="">{t('booking.choose')}</option>}
        {connected.map((c) => (
          <option key={c} value={c}>
            {t(`couriers.names.${c}`)}
          </option>
        ))}
      </select>
    </label>
  )
}
