/**
 * Spellings the API accepts: 01712345678, +8801712345678, +880 1712-345678, 01712-345678 ...
 * plus the fictional demo block 01000000xxx (010 is not issued to any operator; see Phone.DemoBlock).
 */
export const bdMobilePattern =
  /^(?:\+?88)?[\s-]?0[\s-]?1(?:[3-9](?:[\s-]?\d){8}|0(?:[\s-]?0){5}(?:[\s-]?\d){3})$/

/**
 * The local 11-digit form ("01712345678"), or null when it is not a Bangladeshi mobile number.
 * Used as a cache key, so "+880 1712-345678" and "01712345678" share one lookup.
 */
export function normalizePhone(value: string): string | null {
  const trimmed = value.trim()
  if (!bdMobilePattern.test(trimmed)) return null
  const digits = trimmed.replace(/\D/g, '')
  return digits.slice(-11)
}
