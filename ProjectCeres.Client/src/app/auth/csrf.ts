// Module-level memo. Populated by ensureCsrfToken() in api-client.ts on the
// first state-changing request (via GET /api/auth/csrf). The request token is
// an opaque Data-Protector-encrypted value from ASP.NET's IAntiforgery; the
// SPA never decodes it — it just echoes it back as the X-XSRF-TOKEN request
// header on subsequent POST/PUT/PATCH/DELETE requests.
//
// ASP.NET's IAntiforgery validates a cryptographic pair:
//   - cookie token  → travels in the __Host-XSRF cookie (Set-Cookie, httpOnly=false)
//   - request token → emitted by /api/auth/csrf in the X-XSRF-TOKEN response header
//
// The two values are different; sending the cookie value as the header is a
// mismatch that antiforgery validation rejects with 400 (the pre-fix bug).
let cachedRequestToken: string | null = null;

export function getCachedXsrfRequestToken(): string | null {
  return cachedRequestToken;
}

export function setCachedXsrfRequestToken(token: string | null): void {
  cachedRequestToken = token;
}

// Test helper — resets the module-level cache. Tests that exercise the
// handshake should call this in their beforeEach/afterEach to avoid
// cross-test pollution.
export function clearXsrfTokenCacheForTests(): void {
  cachedRequestToken = null;
}
