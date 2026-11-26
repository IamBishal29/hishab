/** Counts and quantities in the reader's digits ("১২" in bn). Not for identifiers: order numbers stay ASCII like on courier slips. */
export function formatNumber(value: number, language: string): string {
  return new Intl.NumberFormat(language === 'bn' ? 'bn-BD' : 'en-BD').format(value)
}
