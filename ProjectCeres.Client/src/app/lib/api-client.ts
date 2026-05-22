import { getCachedXsrfRequestToken, setCachedXsrfRequestToken } from '../auth/csrf';

export class ReauthRequiredError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'ReauthRequiredError';
  }
}

// Global subscription seam for "the session expired silently between requests".
// AuthProvider registers a callback at mount; any non-REAUTH_REQUIRED 401 from
// apiFetch invokes it so AuthContext can drop to 'anon' and RequireAuth can
// redirect on the next render. Without this, a session that expires while the
// user is on a page survives a client-side route change (RequireAuth only
// re-reads the cached status from context) — the user only gets bounced when
// /api/auth/me is re-called, which happens on full page reload but not on
// React Router navigation.
//
// /api/auth/me and /api/auth/csrf are exempt: the first IS the auth probe
// (its 401 is the normal anon path AuthProvider already handles), the second
// is the CSRF bootstrap and its 401 doesn't carry session-expiry semantics.
type UnauthenticatedHandler = () => void;
let unauthenticatedHandler: UnauthenticatedHandler | null = null;
const AUTH_PROBE_URLS = ['/api/auth/me', '/api/auth/csrf'];

// Server header set by PersistentCookieRotationMiddleware on the 401 it
// intentionally returns while issuing a fresh session cookie. Must match
// SessionConstants.CookieRotatedHeader on the server.
export const COOKIE_ROTATED_HEADER = 'x-ceres-cookie-rotated';

export function setOnUnauthenticated(handler: UnauthenticatedHandler | null): void {
  unauthenticatedHandler = handler;
}

/**
 * Dispatch the silently-expired-session signal for a 401 response IF the URL
 * is not an auth-probe and a handler is registered. Public so callers that
 * don't go through apiFetch (notably useApi, which uses raw fetch for GET
 * data-fetching) can opt into the same auth-context notification.
 *
 * Safe to call unconditionally on any 401: the function short-circuits on
 * auth-probe URLs, when no handler is registered, AND when the response
 * carries the X-Ceres-Cookie-Rotated header (the Remember-Me rotation
 * one-extra-round-trip — see PersistentCookieRotationMiddleware comment at
 * line 14-20; the server returned 401 specifically so the browser would
 * retry with the freshly-issued session cookie, NOT because the user is
 * actually logged out).
 */
export function notifyUnauthenticatedIfApplicable(url: string, response?: Response): void {
  if (unauthenticatedHandler === null) return;
  if (AUTH_PROBE_URLS.some((probe) => url === probe || url.startsWith(`${probe}?`))) return;
  if (response?.headers.get(COOKIE_ROTATED_HEADER) === 'true') return;
  unauthenticatedHandler();
}

export class NetworkError extends Error {
  cause?: unknown;
  constructor(message: string, cause?: unknown) {
    super(message);
    this.name = 'NetworkError';
    this.cause = cause;
  }
}

type ProjectErrorEnvelope = {
  error: {
    code: string;
    message: string;
    details?: Array<{ field: string; message: string }>;
  };
};

export type ApiSuccess<T> = { ok: true; status: number; data: T | null };
export type ApiFieldErrorsFailure = {
  ok: false;
  status: 422;
  code: string;
  message: string;
  fieldErrors: Record<string, string>;
};
export type ApiFormErrorFailure = {
  ok: false;
  status: 422;
  code: string;
  message: string;
  formError: string;
};
export type ApiGenericFailure = {
  ok: false;
  status: number;
  code: string;
  message: string;
};
export type ApiFailure = ApiFieldErrorsFailure | ApiFormErrorFailure | ApiGenericFailure;
export type ApiResult<T> = ApiSuccess<T> | ApiFailure;

export type ApiFetchInit = Omit<RequestInit, 'body'> & {
  body?: unknown; // serialised to JSON if not already a string/FormData/Blob
};

const STATE_CHANGING_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

let csrfHandshakeInFlight: Promise<void> | null = null;

async function ensureCsrfToken(): Promise<void> {
  if (getCachedXsrfRequestToken()) return;
  if (csrfHandshakeInFlight) return csrfHandshakeInFlight;
  csrfHandshakeInFlight = (async () => {
    try {
      const response = await fetch('/api/auth/csrf', { method: 'GET', credentials: 'include' });
      const token = response.headers.get('X-XSRF-TOKEN');
      if (token) setCachedXsrfRequestToken(token);
    } finally {
      csrfHandshakeInFlight = null;
    }
  })();
  return csrfHandshakeInFlight;
}

/**
 * Project Ceres SPA fetch wrapper. Handles:
 *  - CSRF handshake (GET /api/auth/csrf) on first state-changing request, deduped
 *  - X-XSRF-TOKEN header on POST/PUT/PATCH/DELETE
 *  - credentials: 'include' so cookies travel
 *  - Project error envelope mapping (422 → field errors / form error,
 *    401 REAUTH_REQUIRED → ReauthRequiredError throw, others → ApiFailure)
 *
 * Generic T is the success body shape; default `unknown` for endpoints
 * returning 204.
 */
export async function apiFetch<T = unknown>(
  url: string,
  init: ApiFetchInit = {},
): Promise<ApiResult<T>> {
  const method = (init.method ?? 'GET').toUpperCase();
  const headers: Record<string, string> = { ...(init.headers as Record<string, string> | undefined) };

  if (STATE_CHANGING_METHODS.has(method)) {
    await ensureCsrfToken();
    const token = getCachedXsrfRequestToken();
    if (token) headers['X-XSRF-TOKEN'] = token;
  }

  let body: BodyInit | undefined;
  if (init.body !== undefined && init.body !== null) {
    if (typeof init.body === 'string' || init.body instanceof FormData || init.body instanceof Blob) {
      body = init.body;
    } else {
      body = JSON.stringify(init.body);
      headers['Content-Type'] = headers['Content-Type'] ?? 'application/json';
    }
  }

  let response: Response;
  try {
    response = await fetch(url, {
      ...init,
      method,
      headers,
      body,
      credentials: 'include',
    });
  } catch (err) {
    throw new NetworkError('Network request failed', err);
  }

  if (response.status === 204) {
    return { ok: true, status: 204, data: null };
  }

  const contentType = response.headers.get('Content-Type') ?? '';
  const isJson = contentType.includes('application/json');
  const payload = isJson ? await response.json().catch(() => null) : null;

  if (response.ok) {
    return { ok: true, status: response.status, data: payload as T };
  }

  // Error path — try to read the project envelope.
  const envelope = (payload as ProjectErrorEnvelope | null)?.error;
  const code = envelope?.code ?? `HTTP_${response.status}`;
  const message = envelope?.message ?? response.statusText;

  if (response.status === 401 && code === 'REAUTH_REQUIRED') {
    throw new ReauthRequiredError(message);
  }

  // Silently-expired-session signal — any plain 401 (not the re-auth gate, not
  // an auth-probe URL, not the Remember-Me rotation handshake) means the cookie
  // the browser sent was no longer valid. Notify the auth context so it can
  // flip status to 'anon'; the matching RequireAuth render on the next route
  // change will redirect to /login.
  if (response.status === 401 && code !== 'REAUTH_REQUIRED') {
    notifyUnauthenticatedIfApplicable(url, response);
  }

  if (response.status === 422 && envelope) {
    if (envelope.details && envelope.details.length > 0) {
      const fieldErrors: Record<string, string> = {};
      for (const d of envelope.details) {
        fieldErrors[d.field] = d.message;
      }
      return { ok: false, status: 422, code, message, fieldErrors };
    }
    return { ok: false, status: 422, code, message, formError: message };
  }

  return { ok: false, status: response.status, code, message };
}
