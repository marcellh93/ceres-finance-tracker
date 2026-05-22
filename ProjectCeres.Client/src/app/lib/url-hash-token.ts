/**
 * Parse a `#token=<raw>` hash fragment and return the raw token, or null if
 * the fragment is empty or omits the `token` key. Used by both PasswordReset
 * and EmailVerify pages to read the one-time token the server emails as part
 * of the verification link.
 */
export function readTokenFromHash(hash: string): string | null {
  const stripped = hash.startsWith('#') ? hash.slice(1) : hash;
  if (!stripped) return null;
  const params = new URLSearchParams(stripped);
  const token = params.get('token');
  return token && token.length > 0 ? token : null;
}
