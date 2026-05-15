import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import en from './locales/en.json';
import es from './locales/es.json';

export const SUPPORTED_LANGUAGES = ['en', 'es'] as const;
export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number];

export const LANG_COOKIE_NAME = 'lang';

/**
 * Read the lang cookie. Returns null if absent or not in SUPPORTED_LANGUAGES.
 * Exposed for use by the language toggle component.
 */
export function readLangCookie(): SupportedLanguage | null {
  if (typeof document === 'undefined') return null;
  const match = document.cookie.match(/(?:^|;\s*)lang=([^;]+)/);
  if (!match) return null;
  const value = decodeURIComponent(match[1]);
  return (SUPPORTED_LANGUAGES as readonly string[]).includes(value)
    ? (value as SupportedLanguage)
    : null;
}

/**
 * Write the lang cookie. SameSite=Lax, Secure, NOT HttpOnly (the SPA reads it),
 * 1-year expiry. Called by the language toggle when the user switches.
 */
export function writeLangCookie(lang: SupportedLanguage): void {
  if (typeof document === 'undefined') return;
  const oneYearSeconds = 60 * 60 * 24 * 365;
  document.cookie = `${LANG_COOKIE_NAME}=${encodeURIComponent(lang)}; Path=/; Max-Age=${oneYearSeconds}; SameSite=Lax; Secure`;
}

void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: { en: { translation: en }, es: { translation: es } },
    fallbackLng: 'en',
    supportedLngs: [...SUPPORTED_LANGUAGES],
    detection: {
      order: ['cookie', 'navigator'],
      lookupCookie: LANG_COOKIE_NAME,
      caches: ['cookie'],
      cookieMinutes: 60 * 24 * 365, // 1 year
      cookieOptions: { path: '/', sameSite: 'lax', secure: true },
    },
    interpolation: { escapeValue: false }, // React already escapes
  });

export default i18n;
