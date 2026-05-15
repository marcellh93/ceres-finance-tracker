import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import en from './locales/en.json';
import es from './locales/es.json';
import { LANG_COOKIE_NAME, SUPPORTED_LANGUAGES } from './lang-cookie';

export { SUPPORTED_LANGUAGES, type SupportedLanguage, LANG_COOKIE_NAME, readLangCookie, writeLangCookie } from './lang-cookie';

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
      cookieOptions: { path: '/', sameSite: 'lax', secure: true, maxAge: 60 * 60 * 24 * 365 },
    },
    interpolation: { escapeValue: false }, // React already escapes
  });

export default i18n;
