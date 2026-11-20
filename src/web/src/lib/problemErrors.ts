import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { ApiProblem } from '@/api/http'

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
  for (const [key, messages] of Object.entries(error.errors)) {
    const path = toFieldPath(key)
    const known = fields.some((f) => path === f || path.startsWith(`${f}.`))
    if (known && messages[0]) {
      setError(path as Path<T>, { message: messages[0] })
      matched = true
    }
  }

  if (!matched) setError('root', { message: error.detail ?? error.title ?? 'errors.unexpected' })
}
