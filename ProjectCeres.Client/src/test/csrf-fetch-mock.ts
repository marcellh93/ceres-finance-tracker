import { vi } from 'vitest';

/**
 * Test helper for components that call the API through `apiFetch`.
 *
 * `apiFetch` performs a one-time CSRF handshake (GET /api/auth/csrf) before its
 * first state-changing request and caches the request token module-side. A test
 * that stubs `global.fetch` with `mockResolvedValueOnce(...)` would otherwise
 * have that first queued response swallowed by the handshake.
 *
 * `installCsrfFetchMock()` returns a mock that answers the handshake itself and
 * forwards every other call to the queue, so `mockResolvedValueOnce` chains keep
 * lining up with the requests the test actually cares about.
 */
export const TEST_CSRF_TOKEN = 'test-xsrf-token';

type FetchMock = ReturnType<typeof vi.fn>;

function csrfHandshakeResponse() {
  return {
    ok: true,
    status: 204,
    headers: {
      get: (name: string) => (name === 'X-XSRF-TOKEN' ? TEST_CSRF_TOKEN : null),
    },
    json: async () => null,
  };
}

export function isCsrfHandshakeUrl(url: unknown): boolean {
  return typeof url === 'string' && url.startsWith('/api/auth/csrf');
}

/**
 * Builds a minimal object with the parts of `Response` that `apiFetch` reads:
 * `ok`, `status`, `headers.get()` and `json()`. A bare `{ ok, status }` literal
 * is not a usable stub — a real Response always has headers, and apiFetch
 * reads Content-Type before deciding whether to parse a body.
 */
export function stubResponse(
  init: { ok?: boolean; status: number; json?: () => Promise<unknown> },
): unknown {
  const hasJson = typeof init.json === 'function';
  return {
    ok: init.ok ?? (init.status >= 200 && init.status < 300),
    status: init.status,
    headers: {
      get: (name: string) =>
        name === 'Content-Type' && hasJson ? 'application/json' : null,
    },
    json: init.json ?? (async () => null),
  };
}

/**
 * Back-fills the Response surface `apiFetch` relies on but raw `fetch` stubs
 * commonly omit: `headers.get(...)` and `json()`. Existing tests were written
 * against raw fetch (which the components called directly) and only stub
 * `{ ok, status, json }`; apiFetch additionally reads `response.headers`.
 * Normalising here keeps those tests' assertions untouched.
 */
function normalizeStubResponse(value: unknown): unknown {
  if (!value || typeof value !== 'object') return value;
  const r = value as Record<string, unknown>;
  if (!('ok' in r) && !('status' in r)) return value;

  if (!r.headers) {
    const isJson = typeof r.json === 'function';
    r.headers = {
      get: (name: string) =>
        name === 'Content-Type' && isJson ? 'application/json' : null,
    };
  }
  if (typeof r.json !== 'function') {
    r.json = async () => null;
  }
  return r;
}

/**
 * Installs a `global.fetch` mock that transparently serves the CSRF handshake.
 * The returned mock records only the calls the test made through apiFetch, with
 * handshake calls filtered out of `appCalls`.
 */
export function installCsrfFetchMock(): FetchMock & {
  appCalls: () => unknown[][];
  callsTo: (method: string) => unknown[][];
} {
  const inner = vi.fn();

  const mock = vi.fn(async (url: unknown, init?: RequestInit) => {
    if (isCsrfHandshakeUrl(url)) return csrfHandshakeResponse();
    return normalizeStubResponse(await inner(url, init));
  }) as FetchMock & {
    appCalls: () => unknown[][];
    callsTo: (method: string) => unknown[][];
  };

  // Queue helpers delegate to the inner mock so `mockResolvedValueOnce` chains
  // are consumed only by real application requests.
  mock.mockResolvedValueOnce = ((value: unknown) => {
    inner.mockResolvedValueOnce(value);
    return mock;
  }) as typeof mock.mockResolvedValueOnce;

  mock.mockResolvedValue = ((value: unknown) => {
    inner.mockResolvedValue(value);
    return mock;
  }) as typeof mock.mockResolvedValue;

  mock.mockImplementation = ((fn: (...args: unknown[]) => unknown) => {
    inner.mockImplementation(fn);
    return mock;
  }) as typeof mock.mockImplementation;

  mock.mockImplementationOnce = ((fn: (...args: unknown[]) => unknown) => {
    inner.mockImplementationOnce(fn);
    return mock;
  }) as typeof mock.mockImplementationOnce;

  mock.mockRejectedValueOnce = ((value: unknown) => {
    inner.mockRejectedValueOnce(value);
    return mock;
  }) as typeof mock.mockRejectedValueOnce;

  mock.appCalls = () => mock.mock.calls.filter((c) => !isCsrfHandshakeUrl(c[0]));
  mock.callsTo = (method: string) =>
    mock.appCalls().filter((c) => (c[1] as RequestInit | undefined)?.method === method);

  globalThis.fetch = mock as unknown as typeof fetch;
  return mock;
}

/**
 * Clears the module-level CSRF token cache between tests so each test exercises
 * the handshake path rather than inheriting a token from a previous test.
 */
export async function resetCsrfCache(): Promise<void> {
  const { clearXsrfTokenCacheForTests } = await import('../app/auth/csrf');
  clearXsrfTokenCacheForTests();
}

/**
 * Seeds the CSRF token cache so `apiFetch` skips the handshake entirely.
 *
 * For tests that install their own `fetch` stub per-test (vi.stubGlobal, or a
 * direct `global.fetch = …`) and assert on call counts or `mock.calls[0]`.
 * With the token pre-cached, the only call the stub sees is the request the
 * test is about — no handshake to filter out, so existing assertions hold
 * unchanged while the component still sends X-XSRF-TOKEN.
 *
 * Call it in `beforeEach` AFTER any resetCsrfCache().
 */
export async function primeCsrfToken(): Promise<void> {
  const { setCachedXsrfRequestToken } = await import('../app/auth/csrf');
  setCachedXsrfRequestToken(TEST_CSRF_TOKEN);
}
