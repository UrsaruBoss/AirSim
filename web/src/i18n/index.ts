// i18n setup: English is the base language, Romanian the secondary one.
// The chosen language is kept in localStorage; on first visit the browser language decides.
import i18n from 'i18next'
import LanguageDetector from 'i18next-browser-languagedetector'
import { initReactI18next } from 'react-i18next'
import en from './en.json'
import ro from './ro.json'

export const LANGUAGES = [
  { code: 'en', label: 'EN' },
  { code: 'ro', label: 'RO' },
] as const

void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: { en: { translation: en }, ro: { translation: ro } },
    fallbackLng: 'en',
    supportedLngs: ['en', 'ro'],
    nonExplicitSupportedLngs: true, // "ro-RO" -> "ro"
    interpolation: { escapeValue: false }, // React already escapes
    detection: { order: ['localStorage', 'navigator'], lookupLocalStorage: 'airsim.lang', caches: ['localStorage'] },
  })

// keeps <html lang> in sync (accessibility, hyphenation, date pickers)
const syncHtmlLang = (lng: string) => {
  document.documentElement.lang = lng.startsWith('ro') ? 'ro' : 'en'
}
syncHtmlLang(i18n.language || 'en')
i18n.on('languageChanged', syncHtmlLang)

/** Locale for dates/numbers, matching the UI language. */
export const currentLocale = () => (i18n.language?.startsWith('ro') ? 'ro-RO' : 'en-GB')

export default i18n
