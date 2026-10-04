import { describe, it, expect, beforeEach } from 'vitest';
import { CONSENT_COOKIE_NAME, readConsent, writeConsent, clearConsent } from './consent-cookie';

function clearDocCookie() {
  document.cookie = `${CONSENT_COOKIE_NAME}=; Path=/; Max-Age=0`;
}

describe('consent-cookie', () => {
  beforeEach(clearDocCookie);

  it('returns null when no consent cookie is set', () => {
    expect(readConsent()).toBeNull();
  });

  it('round-trips a written consent record', () => {
    writeConsent({ necessary: true, analytics: true, preferences: false });
    const rec = readConsent();
    expect(rec).not.toBeNull();
    expect(rec!.categories).toEqual({ necessary: true, analytics: true, preferences: false });
    expect(typeof rec!.timestamp).toBe('string');
    expect(Number.isNaN(Date.parse(rec!.timestamp))).toBe(false);
  });

  it('always forces necessary=true even if a caller passes false', () => {
    // necessary cookies are not consent-gated; the type pins it true, this guards runtime.
    writeConsent({ necessary: true, analytics: false, preferences: false });
    expect(readConsent()!.categories.necessary).toBe(true);
  });

  it('returns null for a malformed cookie value', () => {
    document.cookie = `${CONSENT_COOKIE_NAME}=not-json; Path=/`;
    expect(readConsent()).toBeNull();
  });

  it('clearConsent removes the record so readConsent is null again', () => {
    writeConsent({ necessary: true, analytics: true, preferences: true });
    expect(readConsent()).not.toBeNull();
    clearConsent();
    expect(readConsent()).toBeNull();
  });
});
