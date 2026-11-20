/** BDT amounts in the reader's language (Bangla digits in bn). Display only: the API computes totals. */
export function formatMoney(amount: number, language: string): string {
  return new Intl.NumberFormat(language === 'bn' ? 'bn-BD' : 'en-BD', {
    style: 'currency',
    currency: 'BDT',
    maximumFractionDigits: 2,
  }).format(amount)
}
