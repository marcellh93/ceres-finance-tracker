const COOKIE_NAME = '__Host-XSRF';

export function readXsrfToken(): string | null {
  if (typeof document === 'undefined') return null;
  const match = document.cookie.match(new RegExp(`(?:^|;\\s*)${COOKIE_NAME}=([^;]+)`));
  return match ? decodeURIComponent(match[1]) : null;
}

export function clearXsrfTokenCacheForTests(): void {
  // Reset module state for tests; intentionally a no-op outside tests.
  // (No module state to reset in this minimal helper — kept for forward
  // compatibility if we add a memo later.)
}
