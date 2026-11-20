import { normalizePhone } from './phone'
import { toFieldPath } from './problemErrors'

describe('normalizePhone', () => {
  it.each(['01712345678', '+8801712345678', '8801712345678', '+880 1712-345678', '01712-345678'])(
    'reads %s as one number',
    (input) => {
      expect(normalizePhone(input)).toBe('01712345678')
    },
  )

  it.each(['', '12345', '0171234567', '01212345678', '+1 555 0100'])('rejects %s', (input) => {
    expect(normalizePhone(input)).toBeNull()
  })
})

describe('toFieldPath', () => {
  it('turns API list keys into form paths', () => {
    expect(toFieldPath('lines[0].quantity')).toBe('lines.0.quantity')
    expect(toFieldPath('lines[12].variantId')).toBe('lines.12.variantId')
    expect(toFieldPath('phone')).toBe('phone')
  })
})
