import bn from './locales/bn.json'
import en from './locales/en.json'

type Messages = { [key: string]: string | Messages }

const locales: Record<string, Messages> = { en, bn }
const pluralSuffix = /_(zero|one|two|few|many|other)$/

function flatten(messages: Messages, prefix = ''): Map<string, string> {
  const out = new Map<string, string>()
  for (const [key, value] of Object.entries(messages)) {
    if (typeof value === 'string') out.set(prefix + key, value)
    else for (const [k, v] of flatten(value, `${prefix}${key}.`)) out.set(k, v)
  }
  return out
}

/** "{{count, number}}" -> "count"; the format may differ per language, the variable may not. */
function placeholders(text: string): string[] {
  return [...text.matchAll(/\{\{\s*(\w+)[^}]*\}\}/g)].map((m) => m[1] ?? '').sort()
}

/** Plural variants collapse to one base key: en has _one/_other, other languages may need more. */
function byBaseKey(messages: Map<string, string>): Map<string, string[]> {
  const out = new Map<string, string[]>()
  for (const [key, value] of messages) {
    const base = key.replace(pluralSuffix, '')
    out.set(base, [...(out.get(base) ?? []), value])
  }
  return out
}

const flat = Object.fromEntries(Object.entries(locales).map(([lng, m]) => [lng, flatten(m)]))
const english = byBaseKey(flat.en ?? new Map<string, string>())

describe.each(Object.keys(locales).filter((lng) => lng !== 'en'))('%s translations', (lng) => {
  const messages = flat[lng] ?? new Map<string, string>()
  const translated = byBaseKey(messages)

  it('has every English key and no keys English does not have', () => {
    expect([...translated.keys()].filter((k) => !english.has(k))).toEqual([])
    expect([...english.keys()].filter((k) => !translated.has(k))).toEqual([])
  })

  it('uses the same placeholders as English', () => {
    const mismatched = [...english].filter(([key, values]) => {
      const theirs = translated.get(key)
      return (
        theirs !== undefined &&
        placeholders(theirs[0] ?? '').join() !== placeholders(values[0] ?? '').join()
      )
    })
    expect(mismatched.map(([key]) => key)).toEqual([])
  })

  it('has every plural form the language needs', () => {
    const categories = new Intl.PluralRules(lng).resolvedOptions().pluralCategories
    const pluralBases = new Set(
      [...messages.keys()]
        .filter((k) => pluralSuffix.test(k))
        .map((k) => k.replace(pluralSuffix, '')),
    )
    const missing = [...pluralBases].flatMap((base) =>
      categories.filter((c) => !messages.has(`${base}_${c}`)).map((c) => `${base}_${c}`),
    )
    expect(missing).toEqual([])
  })
})

describe.each(Object.keys(locales))('%s messages', (lng) => {
  const messages = flat[lng] ?? new Map<string, string>()

  it('has no empty text', () => {
    expect([...messages].filter(([, v]) => v.trim() === '').map(([k]) => k)).toEqual([])
  })

  it('formats counts for the reader (Bangla digits in bn)', () => {
    expect([...messages].filter(([, v]) => /\{\{\s*count\s*\}\}/.test(v)).map(([k]) => k)).toEqual(
      [],
    )
  })
})
