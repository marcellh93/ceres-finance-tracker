import { readXsrfToken } from '../auth/csrf';

export class ReauthRequiredError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'ReauthRequiredError';
  }
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
export type ApiFailure =
  | { ok: false; status: number; code: string; message: string; fieldErrors?: Record<string, string>; formError?: string };
export type ApiResult<T> = ApiSuccess<T> | ApiFailure;

export type ApiFetchInit = Omit<RequestInit, 'body'> & {
  body?: unknown; // serialised to JSON if not already a string/FormData/Blob
};

const STATE_CHANGING_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

let csrfHandshakeInFlight: Promise<void> | null = null;

async function ensureCsrfToken(): Promise<void> {
  if (readXsrfToken()) return;
  if (csrfHandshakeInFlight) return csrfHandshakeInFlight;
  csrfHandshakeInFlight = (async () => {
    try {
      await fetch('/api/auth/csrf', { method: 'GET', credentials: 'include' });
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
    const token = readXsrfToken();
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
