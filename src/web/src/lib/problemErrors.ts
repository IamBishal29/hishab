import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { ApiProblem } from '@/api/http'

/**
 * One message for an action that has no form field to show it under (book, print, disconnect):
 * the first server field error ("Book these orders with a courier first: #12"), else the
 * ProblemDetails detail, else an i18n key for a network failure.
 */
export function problemMessage(error: unknown): string {
  if (!(error instanceof ApiProblem)) return 'errors.network'
  const first = Object.values(error.errors).find((messages) => messages.length > 0)?.[0]
  return first ?? error.detail ?? error.title ?? 'errors.unexpected'
}

/** The API keys list items as `lines[0].quantity`; React Hook Form names them `lines.0.quantity`. */
export const toFieldPath = (key: string): string => key.replace(/\[(\d+)\]/g, '.$1')

/**
 * Shows server ProblemDetails errors under the matching form fields (the API keys them by
 * camelCase field name, like the form). A key matches a listed field or a path inside it
 * (`lines` covers `lines.0.quantity`). Anything that matches no field goes to `root`.
 */
export function applyProblem<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  fields: readonly Path<T>[],
): void {
  if (!(error instanceof ApiProblem)) {
    setError('root', { message: 'errors.network' })
    return
  }

  let matched = false
  let unmatched: string | undefined
  for (const [key, messages] of Object.entries(error.errors)) {
    const path = toFieldPath(key)
    const known = fields.some((f) => path === f || path.startsWith(`${f}.`))
    if (known && messages[0]) {
      setError(path as Path<T>, { message: messages[0] })
      matched = true
    } else {
      unmatched ??= messages[0]
    }
  }

  // A server reason with no field on this form ("The courier rejected these keys.") must still
  // be shown: as the form's own error, rather than the generic title.
  if (unmatched) setError('root', { message: unmatched })
  else if (!matched)
    setError('root', { message: error.detail ?? error.title ?? 'errors.unexpected' })
}
