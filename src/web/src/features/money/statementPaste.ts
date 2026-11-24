/** One statement line as the payout form holds it (amounts as typed text, like the inputs). */
export interface StatementRow {
  consignmentId: string
  collected: string
  deliveryFee: string
  codFee: string
}

export interface PasteResult {
  rows: StatementRow[]
  /** 1-based line numbers that were not blank but could not be read (shown to the seller). */
  skipped: number[]
}

/**
 * Reads rows copied from a courier statement: Excel / Google Sheets copy as tab-separated text,
 * a CSV file as comma-separated. Columns, in order: consignment ID, collected, delivery fee,
 * COD fee (the two fees may be missing = 0). A header row is skipped (its amount is not a number).
 * Never guesses: a row it cannot read is reported in `skipped`, not dropped silently.
 */
export function parseStatement(text: string): PasteResult {
  const rows: StatementRow[] = []
  const skipped: number[] = []
  let first = true

  text.split(/\r?\n/).forEach((line, index) => {
    if (line.trim() === '') return
    // Tabs win: a spreadsheet cell may hold "1,200.00", where the comma is a thousands separator.
    const cells = (line.includes('\t') ? line.split('\t') : line.split(',')).map((c) => c.trim())
    const [id = '', collected = '', deliveryFee = '0', codFee = '0'] = cells
    const amounts = [collected, deliveryFee || '0', codFee || '0'].map(toAmount)
    const isFirst = first
    first = false

    if (id === '' || amounts.some((a) => a === null)) {
      // An unreadable first line is the header ("Consignment ID, Amount, ..."); later ones are reported.
      if (!isFirst) skipped.push(index + 1)
      return
    }
    const [c, d, f] = amounts as string[]
    rows.push({ consignmentId: id, collected: c ?? '', deliveryFee: d ?? '', codFee: f ?? '' })
  })

  return { rows, skipped }
}

/** "৳1,200.50" or "1200.5" -> "1200.50"-like text the form validates; null when not an amount. */
function toAmount(cell: string): string | null {
  const plain = cell.replace(/[৳,\s]/g, '').replace(/^(tk|bdt)/i, '')
  return /^\d+(\.\d+)?$/.test(plain) ? plain : null
}
