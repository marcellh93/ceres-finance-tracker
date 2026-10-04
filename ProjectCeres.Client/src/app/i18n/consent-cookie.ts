export const CONSENT_COOKIE_NAME = 'cookie_consent';

export type ConsentCategories = {
  necessary: true;
  analytics: boolean;
  preferences: boolean;
};

export type ConsentRecord = {
  categories: ConsentCategories;
  timestamp: string;
};

/** Read + parse the consent cookie. Returns null if absent or malformed. */
export function readConsent(): ConsentRecord | null {
  if (typeof document === 'undefined') return null;
  const match = document.cookie.match(
    new RegExp(`(?:^|;\\s*)${CONSENT_COOKIE_NAME}=([^;]+)`),
  );
  if (!match) return null;
  try {
    const parsed = JSON.parse(decodeURIComponent(match[1])) as ConsentRecord;
    if (!parsed || typeof parsed !== 'object' || !parsed.categories) return null;
    return parsed;
  } catch {
    return null;
  }
}

/** Write the consent record. 24-month Max-Age (AEPD re-consent), first-party,
 *  SameSite=Lax, Secure, NOT HttpOnly (the SPA reads it), no identifier. */
export function writeConsent(categories: ConsentCategories): void {
  if (typeof document === 'undefined') return;
  const twentyFourMonthsSeconds = 60 * 60 * 24 * 365 * 2;
  const record: ConsentRecord = {
    categories: { ...categories, necessary: true },
    timestamp: new Date().toISOString(),
  };
  const value = encodeURIComponent(JSON.stringify(record));
  document.cookie = `${CONSENT_COOKIE_NAME}=${value}; Path=/; Max-Age=${twentyFourMonthsSeconds}; SameSite=Lax; Secure`;
}

/** Expire the cookie so the banner re-shows (GDPR Art. 7(3) withdrawal). */
export function clearConsent(): void {
  if (typeof document === 'undefined') return;
  document.cookie = `${CONSENT_COOKIE_NAME}=; Path=/; Max-Age=0; SameSite=Lax; Secure`;
}
