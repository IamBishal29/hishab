import { formatNumber } from '@/lib/numbers'
import i18n, { languageStorageKey } from '.'

async function freshI18n() {
  vi.resetModules() // a new page load: the module reads storage again
  return (await import('.')).default
}

describe('language choice', () => {
  it('starts in Bangla on a first visit', async () => {
    localStorage.clear()
    const fresh = await freshI18n()

    expect(fresh.language).toBe('bn')
    expect(document.documentElement.lang).toBe('bn')
  })

  it('remembers the seller’s choice across reloads', async () => {
    await i18n.changeLanguage('en')
    expect(localStorage.getItem(languageStorageKey)).toBe('en')
    expect(document.documentElement.lang).toBe('en')

    const fresh = await freshI18n()
    expect(fresh.language).toBe('en')
  })

  it('ignores a saved value it does not support', async () => {
    localStorage.setItem(languageStorageKey, 'fr')
    const fresh = await freshI18n()

    expect(fresh.language).toBe('bn')
  })
})

describe('numbers in the reader’s digits', () => {
  it('writes counts inside sentences with Bangla digits in bn and keeps plurals', async () => {
    await i18n.changeLanguage('bn')
    expect(i18n.t('orders.items', { count: 12 })).toContain('১২')

    await i18n.changeLanguage('en')
    expect(i18n.t('orders.items', { count: 1 })).toBe('1 item')
    expect(i18n.t('orders.items', { count: 1200 })).toBe('1,200 items')
  })

  it('formats bare counts the same way', () => {
    expect(formatNumber(1234, 'bn')).toBe('১,২৩৪')
    expect(formatNumber(1234, 'en')).toBe('1,234')
  })
})
