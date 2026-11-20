import { parseFilters, toApiParams, toSearchParams } from './orderFilters'

const parse = (query: string) => parseFilters(new URLSearchParams(query))

describe('order filters in the URL', () => {
  it('round-trips every filter through the URL', () => {
    const query =
      'status=New&status=Confirmed&from=2026-11-01&to=2026-11-20&area=outside&source=Facebook&q=Rahima'
    expect(toSearchParams(parse(query)).toString()).toBe(query)
  })

  it('drops values a hand-edited link could contain instead of sending them to the API', () => {
    expect(parse('status=Lost&status=New&status=New&from=yesterday&area=north&source=Fax')).toEqual(
      {
        status: ['New'],
        from: null,
        to: null,
        area: null,
        source: null,
        q: '',
      },
    )
  })

  it('maps filters to the API parameters and leaves defaults out', () => {
    expect(toApiParams(parse(''))).toEqual({})
    expect(toApiParams(parse('status=New&area=inside&q=%231043&from=2026-11-01'))).toEqual({
      Status: ['New'],
      IsInsideDhaka: true,
      Search: '#1043',
      From: '2026-11-01',
    })
  })
})
