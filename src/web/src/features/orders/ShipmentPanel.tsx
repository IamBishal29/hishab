import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import {
  getGetApiOrdersIdQueryKey,
  LabelFormat,
  LocationLevel,
  OrderStatus,
  postApiOrdersIdBook,
  ShipmentState,
  type CourierDestination,
  type CourierProvider,
  type OrderDetails,
} from '@/api/generated/hishab'
import { Button } from '@/components/ui/button'
import { canPack } from '@/features/auth/permissions'
import { useSession } from '@/features/auth/useSession'
import { useBookingChoice } from '@/features/couriers/courierQueries'
import { CourierSelect } from '@/features/couriers/CourierSelect'
import { DestinationPicker, type DestinationDraft } from '@/features/couriers/DestinationPicker'
import { isComplete, useLocations, useQuote } from '@/features/couriers/locationQueries'
import { formatMoney } from '@/lib/money'
import { problemMessage } from '@/lib/problemErrors'
import { useDownloadLabels } from './labels'
import { invalidateOrders } from './orderQueries'

/** The courier side of an order: book it when confirmed, then its consignment and label. */
export function ShipmentPanel({ details }: { details: OrderDetails }): ReactElement | null {
  const { t, i18n } = useTranslation()
  const { data: session } = useSession()
  const { order, shipment } = details
  const mayPack = canPack(session?.role)
  const canBook = mayPack && order.status === OrderStatus.Confirmed
  const labels = useDownloadLabels()

  if (!shipment && !canBook) return null

  return (
    <section aria-label={t('booking.title')} className="flex flex-col gap-3 rounded-lg border p-3">
      <h2 className="font-semibold">{t('booking.title')}</h2>
      {shipment?.state === ShipmentState.Booked && (
        <>
          <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
            <dt className="text-muted-foreground">{t('booking.courier')}</dt>
            <dd>{t(`couriers.names.${shipment.provider}`)}</dd>
            <dt className="text-muted-foreground">{t('booking.consignment')}</dt>
            <dd className="break-all">{shipment.consignmentId}</dd>
            {shipment.destination && (
              <>
                <dt className="text-muted-foreground">{t('destination.title')}</dt>
                <dd>
                  <DestinationNames
                    provider={shipment.provider}
                    destination={shipment.destination}
                  />
                </dd>
              </>
            )}
            {shipment.trackingCode && (
              <>
                <dt className="text-muted-foreground">{t('booking.tracking')}</dt>
                <dd className="break-all">{shipment.trackingCode}</dd>
              </>
            )}
            {shipment.deliveryFee !== null && (
              <>
                <dt className="text-muted-foreground">{t('booking.fee')}</dt>
                <dd>{formatMoney(shipment.deliveryFee, i18n.language)}</dd>
              </>
            )}
          </dl>
          {mayPack && (
            <div className="flex flex-wrap gap-2">
              {Object.values(LabelFormat).map((format) => (
                <Button
                  key={format}
                  size="sm"
                  variant="outline"
                  disabled={labels.isPending}
                  onClick={() => {
                    labels.mutate({ ids: [order.id], format })
                  }}
                >
                  {t(`labels.download${format}`)}
                </Button>
              ))}
            </div>
          )}
          {labels.error && (
            <p role="alert" className="text-sm text-destructive">
              {t(problemMessage(labels.error))}
            </p>
          )}
        </>
      )}
      {shipment?.state === ShipmentState.Rejected && (
        <p role="alert" className="text-sm text-destructive">
          {t('booking.rejected', { message: shipment.error ?? '' })}
        </p>
      )}
      {shipment?.state === ShipmentState.Requested && (
        <p className="text-sm text-muted-foreground">{t('booking.requested')}</p>
      )}
      {canBook && (
        <BookForm
          orderId={order.id}
          shownError={shipment?.state === ShipmentState.Rejected ? shipment.error : null}
        />
      )}
    </section>
  )
}

function BookForm({
  orderId,
  shownError,
}: {
  orderId: string
  /** The rejection already shown above: not repeated as this request's error. */
  shownError: string | null
}): ReactElement {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const choice = useBookingChoice()
  const [picked, setPicked] = useState<CourierProvider | null>(null)
  const [draft, setDraft] = useState<DestinationDraft>({})
  const courier = picked ?? choice.preferred
  const usesLocations = courier !== null && choice.withLocations.includes(courier)
  const destination = usesLocations && isComplete(draft) ? draft : null
  // A city without a zone is neither a destination nor "by address": booking it would silently
  // drop what the packer started to choose.
  const halfChosen = usesLocations && Boolean(draft.cityId) && destination === null
  const quote = useQuote(orderId, courier, destination)

  const book = useMutation({
    mutationFn: (provider: CourierProvider) =>
      postApiOrdersIdBook(orderId, { courier: provider, destination }),
    onSuccess: (updated) => {
      queryClient.setQueryData(getGetApiOrdersIdQueryKey(orderId), updated)
    },
    // Also after a failure: a rejection is stored on the shipment and shown from the refetch.
    onSettled: () => {
      invalidateOrders(queryClient, [orderId])
    },
  })

  if (choice.isPending) return <p className="text-sm">{t('common.loading')}</p>
  return (
    <div className="flex flex-col gap-2">
      <CourierSelect
        connected={choice.connected}
        value={courier}
        onChange={(next) => {
          // Location IDs belong to one courier: another courier's list starts empty.
          setPicked(next)
          setDraft({})
        }}
      />
      {courier && usesLocations && (
        <DestinationPicker
          provider={courier}
          value={draft}
          onChange={setDraft}
          disabled={book.isPending}
        />
      )}
      {usesLocations && !draft.cityId && (
        <p className="text-sm text-muted-foreground">{t('destination.byAddress')}</p>
      )}
      {halfChosen && <p className="text-sm text-muted-foreground">{t('destination.needZone')}</p>}
      {destination && <QuoteLine quote={quote} />}
      {choice.connected.length > 0 && (
        <Button
          className="self-start"
          // Disabled while in flight: a double tap must not send two bookings (the API is
          // idempotent on the order anyway, but the second answer would be a confusing error).
          disabled={courier === null || halfChosen || book.isPending}
          onClick={() => {
            if (courier) book.mutate(courier)
          }}
        >
          {book.isPending ? t('booking.booking') : t('booking.book')}
        </Button>
      )}
      {book.error && problemMessage(book.error) !== shownError && (
        <p role="alert" className="text-sm text-destructive">
          {t(problemMessage(book.error))}
        </p>
      )}
    </div>
  )
}

function QuoteLine({ quote }: { quote: ReturnType<typeof useQuote> }): ReactElement {
  const { t, i18n } = useTranslation()
  if (quote.isError) {
    return (
      <p role="alert" className="text-sm text-destructive">
        {t('destination.quoteFailed', { message: t(problemMessage(quote.error)) })}
      </p>
    )
  }
  if (!quote.data)
    return <p className="text-sm text-muted-foreground">{t('destination.pricing')}</p>
  return (
    // Polite live region: the price changes as the zone changes, and screen readers should hear it.
    <p
      aria-live="polite"
      className={quote.isPlaceholderData ? 'text-sm text-muted-foreground' : 'text-sm'}
    >
      {t('destination.quote', {
        fee: formatMoney(quote.data.deliveryFee, i18n.language),
        cod: formatMoney(quote.data.codCharge, i18n.language),
      })}
    </p>
  )
}

/** "Dhaka / Mirpur / Mirpur 10" from the cached courier lists; the raw ID until a name is known. */
function DestinationNames({
  provider,
  destination,
}: {
  provider: CourierProvider
  destination: CourierDestination
}): ReactElement {
  const cities = useLocations(provider, LocationLevel.City, null)
  const zones = useLocations(provider, LocationLevel.Zone, destination.cityId)
  const areas = useLocations(
    provider,
    LocationLevel.Area,
    destination.areaId ? destination.zoneId : null,
  )
  const name = (list: { id: string; name: string }[], id: string) =>
    list.find((l) => l.id === id)?.name ?? id
  const parts = [name(cities.data, destination.cityId), name(zones.data, destination.zoneId)]
  if (destination.areaId) parts.push(name(areas.data, destination.areaId))
  return <>{parts.join(' / ')}</>
}
