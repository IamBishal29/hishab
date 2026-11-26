import { useId, type ReactElement, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Input } from '@/components/ui/input'

type Props = React.ComponentProps<'input'> & {
  label: string
  /** An i18n key (client validation) or a message from the server (ProblemDetails). */
  error?: string | undefined
  /** Shown after the label text and read with it, e.g. a "Suggested" mark. */
  badge?: ReactNode
}

/** Label, input and error message wired for screen readers (aria-invalid, aria-describedby). */
export function FormField({ label, error, badge, ...input }: Props): ReactElement {
  const { t, i18n } = useTranslation()
  const id = useId()
  const errorId = `${id}-error`
  const message = error && i18n.exists(error) ? t(error) : error

  return (
    <div className="flex flex-col gap-1.5 text-left">
      <label htmlFor={id} className="text-sm font-medium">
        {label}
        {badge}
      </label>
      <Input
        id={id}
        aria-invalid={Boolean(error)}
        aria-describedby={error ? errorId : undefined}
        {...input}
      />
      {message && (
        <p id={errorId} role="alert" className="text-sm text-destructive">
          {message}
        </p>
      )}
    </div>
  )
}
