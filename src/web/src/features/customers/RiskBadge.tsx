import { CircleCheck, CircleDashed, OctagonX, TriangleAlert, type LucideIcon } from 'lucide-react'
import type { ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import { cn } from 'cn'
import type { CustomerRiskLevel, RiskAssessment } from '@/api/generated/hishab'

// Three signals, so no one depends on colour: the word, a distinct icon shape and the colour.
// Colours are 900-on-100 pairs (contrast well above WCAG AA 4.5:1).
const look: Record<CustomerRiskLevel, { icon: LucideIcon; tone: string }> = {
  New: { icon: CircleDashed, tone: 'bg-slate-100 text-slate-900' },
  Safe: { icon: CircleCheck, tone: 'bg-green-100 text-green-900' },
  Watch: { icon: TriangleAlert, tone: 'bg-amber-100 text-amber-900' },
  Risky: { icon: OctagonX, tone: 'bg-red-100 text-red-900' },
}

/** Every reason in words ("Returned 67% of parcels"). */
function useRiskReasons(risk: RiskAssessment): string[] {
  const { t } = useTranslation()
  const rate = risk.returnRate === null ? 0 : Math.round(risk.returnRate * 100)
  return risk.reasons.map((r) => t(`risk.reasons.${r}`, { rate }))
}

interface RiskBadgeProps {
  risk: RiskAssessment
  /** One line (order rows): the level and the first reason. Otherwise every reason is listed. */
  compact?: boolean
}

/** The customer's risk with this shop: New, Safe, Watch or Risky, always with the reason. */
export function RiskBadge({ risk, compact = false }: RiskBadgeProps): ReactElement {
  const { t } = useTranslation()
  const reasons = useRiskReasons(risk)
  const { icon: Icon, tone } = look[risk.level]
  const level = (
    <span
      className={cn(
        'inline-flex shrink-0 items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium',
        tone,
      )}
    >
      <Icon aria-hidden="true" className="size-3.5" />
      {t(`risk.levels.${risk.level}`)}
    </span>
  )

  if (compact) {
    return (
      <span className="flex min-w-0 items-center gap-1.5 text-xs">
        {level}
        {reasons[0] && <span className="truncate text-muted-foreground">{reasons[0]}</span>}
      </span>
    )
  }
  return (
    <div className="flex flex-col gap-1 text-sm" aria-label={t('risk.label')} role="group">
      <div>{level}</div>
      {reasons.length > 0 && (
        <ul className="list-inside list-disc text-muted-foreground">
          {reasons.map((r) => (
            <li key={r}>{r}</li>
          ))}
        </ul>
      )}
    </div>
  )
}
