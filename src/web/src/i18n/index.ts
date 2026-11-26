import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import bn from './locales/bn.json'
import en from './locales/en.json'

export const supportedLanguages = ['en', 'bn'] as const
export type Language = (typeof supportedLanguages)[number]

/** Bangla first (PRD): a first visit gets bn; the seller's own choice is remembered on this device. */
export const defaultLanguage: Language = 'bn'
export const languageStorageKey = 'hishab.language'

function isLanguage(value: unknown): value is Language {
  return supportedLanguages.includes(value as Language)
}

/** The saved choice, if it is one we support (storage is untrusted: another app or an old build may have written it). */
export function savedLanguage(): Language | null {
  try {
    const value = localStorage.getItem(languageStorageKey)
    return isLanguage(value) ? value : null
  } catch {
    return null // storage blocked (private mode, policy): fall back to the default
  }
}

/** Keep <html lang> in step: screen readers pick their voice from it and CSS picks the Bangla font. */
function applyLanguage(language: string): void {
  document.documentElement.lang = language
  try {
    localStorage.setItem(languageStorageKey, language)
  } catch {
    // not persisted; the choice still holds for this tab
  }
}

i18n.on('languageChanged', applyLanguage)

void i18n.use(initReactI18next).init({
  resources: { en: { translation: en }, bn: { translation: bn } },
  lng: savedLanguage() ?? defaultLanguage,
  fallbackLng: 'en',
  supportedLngs: supportedLanguages,
  interpolation: { escapeValue: false }, // React already escapes output
})

export default i18n
