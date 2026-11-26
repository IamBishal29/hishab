import type { OrderDraft } from '@/api/generated/hishab'
import { normalizePhone } from '@/lib/phone'

/** Order form fields the chat assistant may suggest (lines are handled separately). */
export const suggestibleFields = [
  'phone',
  'customerName',
  'deliveryAddress',
  'area',
  'note',
] as const
export type SuggestibleField = (typeof suggestibleFields)[number]

/** What the assistant suggested per field, in the form's own format. */
export type Suggestions = Partial<Record<SuggestibleField, string>>

/** The draft as form values: local phone (as sellers type it), area as the radio value. */
export function draftSuggestions(draft: OrderDraft): Suggestions {
  const suggestions: Suggestions = {}
  const phone = draft.phone ? normalizePhone(draft.phone) : null
  if (phone) suggestions.phone = phone
  if (draft.customerName) suggestions.customerName = draft.customerName
  if (draft.deliveryAddress) suggestions.deliveryAddress = draft.deliveryAddress
  if (draft.isInsideDhaka !== null) suggestions.area = draft.isInsideDhaka ? 'inside' : 'outside'
  if (draft.note) suggestions.note = draft.note
  return suggestions
}

/**
 * The suggestions that may go into the form: a field is filled only if the seller has not
 * touched it, or it still holds an earlier suggestion. What a person typed is never replaced.
 */
export function suggestionsToApply(
  next: Suggestions,
  current: Record<SuggestibleField, string>,
  isDirty: (field: SuggestibleField) => boolean,
  previous: Suggestions,
): Suggestions {
  const apply: Suggestions = {}
  for (const field of suggestibleFields) {
    const value = next[field]
    if (value === undefined) continue
    if (!isDirty(field) || current[field] === previous[field]) apply[field] = value
  }
  return apply
}

/** A field shows as suggested only while it still holds the suggestion: editing it removes the mark. */
export const isSuggested = (
  suggestions: Suggestions,
  field: SuggestibleField,
  value: string,
): boolean => suggestions[field] !== undefined && suggestions[field] === value
