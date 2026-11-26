import { emptyDraft } from '@/test/chatDraftApi'
import { draftSuggestions, isSuggested, suggestionsToApply } from './chatDraft'

const blank = { phone: '', customerName: '', deliveryAddress: '', area: 'inside', note: '' }

describe('chat draft suggestions', () => {
  it('turns a draft into form values: local phone, area as the radio value, empty fields left out', () => {
    const suggestions = draftSuggestions(
      emptyDraft({ phone: '+8801712345678', customerName: 'Rahima', isInsideDhaka: false }),
    )

    expect(suggestions).toEqual({ phone: '01712345678', customerName: 'Rahima', area: 'outside' })
  })

  it('fills untouched fields and earlier suggestions, never what the seller typed', () => {
    const next = { customerName: 'Rahima', deliveryAddress: 'Mirpur 10', note: 'call at 5' }
    const current = { ...blank, customerName: 'Rahima Begum', deliveryAddress: 'Uttara', note: 'x' }
    const dirty = new Set(['customerName', 'deliveryAddress', 'note'])

    const apply = suggestionsToApply(next, current, (f) => dirty.has(f), {
      deliveryAddress: 'Uttara',
    })

    // Name was typed (kept); address still held the last suggestion (replaced); note typed (kept).
    expect(apply).toEqual({ deliveryAddress: 'Mirpur 10' })
  })

  it('marks a field only while it still holds the suggestion', () => {
    expect(isSuggested({ customerName: 'Rahima' }, 'customerName', 'Rahima')).toBe(true)
    expect(isSuggested({ customerName: 'Rahima' }, 'customerName', 'Rahima B')).toBe(false)
    expect(isSuggested({}, 'customerName', '')).toBe(false)
  })
})
