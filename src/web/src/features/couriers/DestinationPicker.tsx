import { useId, useState, type ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import {
  LocationLevel,
  type CourierDestination,
  type CourierLocation,
  type CourierProvider,
} from '@/api/generated/hishab'
import { Input } from '@/components/ui/input'
import { useLocations } from './locationQueries'

/** What the packer has picked so far; a parent change clears everything below it. */
export type DestinationDraft = Partial<CourierDestination>

interface Props {
  provider: CourierProvider
  value: DestinationDraft
  onChange: (value: DestinationDraft) => void
  disabled?: boolean
}

/**
 * City, then zone, then area (optional), each from the courier's own lists. A level appears once
 * its parent is chosen, so a list is only fetched when it can be used.
 */
export function DestinationPicker({ provider, value, onChange, disabled }: Props): ReactElement {
  const { t } = useTranslation()
  const cities = useLocations(provider, LocationLevel.City, null)
  const zones = useLocations(provider, LocationLevel.Zone, value.cityId ?? null)
  const areas = useLocations(provider, LocationLevel.Area, value.zoneId ?? null)

  return (
    <fieldset className="flex flex-col gap-2" disabled={disabled}>
      <legend className="mb-1 text-sm font-medium">{t('destination.title')}</legend>
      <LocationSelect
        label={t('destination.city')}
        list={cities}
        value={value.cityId}
        onChange={(cityId) => {
          onChange({ cityId })
        }}
      />
      {value.cityId && (
        <LocationSelect
          label={t('destination.zone')}
          list={zones}
          value={value.zoneId}
          onChange={(zoneId) => {
            onChange({ cityId: value.cityId, zoneId })
          }}
        />
      )}
      {/* Not every zone has areas: no empty select for those. */}
      {value.zoneId && (areas.isLoading || areas.data.length > 0) && (
        <LocationSelect
          label={t('destination.area')}
          optional
          list={areas}
          value={value.areaId ?? undefined}
          onChange={(areaId) => {
            onChange({ ...value, areaId })
          }}
        />
      )}
    </fieldset>
  )
}

/** Above this many options a filter box helps: Pathao has 60+ cities and some cities 100+ zones. */
const FILTER_FROM = 8

function LocationSelect({
  label,
  list,
  value,
  onChange,
  optional = false,
}: {
  label: string
  list: { data: CourierLocation[]; isLoading: boolean; isError: boolean }
  value: string | undefined
  onChange: (id: string | undefined) => void
  optional?: boolean
}): ReactElement {
  const { t } = useTranslation()
  const id = useId()
  const [filter, setFilter] = useState('')
  const needle = filter.trim().toLowerCase()
  // The chosen option stays in the list even when the filter hides it, so the select keeps
  // showing what is picked.
  const shown = needle
    ? list.data.filter((l) => l.id === value || l.name.toLowerCase().includes(needle))
    : list.data

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-sm font-medium">
        {optional ? t('destination.optional', { label }) : label}
      </label>
      {list.data.length > FILTER_FROM && (
        <Input
          type="search"
          aria-label={t('destination.filter', { label })}
          placeholder={t('destination.filterPlaceholder')}
          value={filter}
          onChange={(e) => {
            setFilter(e.target.value)
          }}
        />
      )}
      <select
        id={id}
        className="h-10 rounded-md border border-input bg-transparent px-2 text-sm"
        value={value ?? ''}
        disabled={list.isLoading || list.isError}
        onChange={(e) => {
          onChange(e.target.value === '' ? undefined : e.target.value)
        }}
      >
        <option value="">
          {list.isLoading
            ? t('common.loading')
            : optional
              ? t('destination.noArea')
              : t('destination.choose')}
        </option>
        {shown.map((l) => (
          <option key={l.id} value={l.id}>
            {l.name}
          </option>
        ))}
      </select>
      {list.isError && (
        <p role="alert" className="text-sm text-destructive">
          {t('destination.loadFailed')}
        </p>
      )}
    </div>
  )
}
