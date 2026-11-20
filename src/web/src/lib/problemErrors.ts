import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { ApiProblem } from '@/api/http'

/**
 * Shows server ProblemDetails errors under the matching form fields (the API keys them by
 * camelCase field name, like the form). Anything that matches no field goes to `root`.
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
    const field = fields.find((f) => f === key)
    if (field && messages[0]) {
      setError(field, { message: messages[0] })
      matched = true
    }
  }

  if (!matched) setError('root', { message: error.detail ?? error.title ?? 'errors.unexpected' })
}
