export const SUPPORTED_LANGUAGES = ['en', 'es'] as const;
export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number];

export const LANG_COOKIE_NAME = 'lang';

/**
 * Read the lang cookie. Returns null if absent or not in SUPPORTED_LANGUAGES.
 * Exposed for use by the language toggle component and the auth context.
 */
export function readLangCookie(): SupportedLanguage | null {
  if (typeof document === 'undefined') return null;
  const match = document.cookie.match(new RegExp(`(?:^|;\\s*)${LANG_COOKIE_NAME}=([^;]+)`));
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
