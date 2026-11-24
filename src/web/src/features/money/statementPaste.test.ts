import { parseStatement } from './statementPaste'

describe('parseStatement', () => {
  it('reads tab-separated rows from a spreadsheet, keeping thousands separators apart', () => {
    const text = 'Consignment\tCollected\tDelivery\tCOD fee\nDH-1\t1,200.00\t60\t12\nDH-2\t৳850\t120\t8.50\n'

    expect(parseStatement(text)).toEqual({
      rows: [
        { consignmentId: 'DH-1', collected: '1200.00', deliveryFee: '60', codFee: '12' },
        { consignmentId: 'DH-2', collected: '850', deliveryFee: '120', codFee: '8.50' },
      ],
      skipped: [],
    })
  })

  it('reads CSV rows and treats missing fees as zero', () => {
    expect(parseStatement('DH-1,500\r\nDH-2,700,60,').rows).toEqual([
      { consignmentId: 'DH-1', collected: '500', deliveryFee: '0', codFee: '0' },
      { consignmentId: 'DH-2', collected: '700', deliveryFee: '60', codFee: '0' },
    ])
  })

  it('reports unreadable rows by line number instead of dropping them', () => {
    const result = parseStatement('DH-1\t500\nDH-2\tpaid\n\n\t300\nDH-5\t-20')

    expect(result.rows.map((r) => r.consignmentId)).toEqual(['DH-1'])
    expect(result.skipped).toEqual([2, 4, 5])
  })

  it('skips only the first line as a header', () => {
    expect(parseStatement('\nID\tAmount\nDH-1\t500').skipped).toEqual([])
    expect(parseStatement('DH-1\t500\nID\tAmount').skipped).toEqual([2])
  })
})
