import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { apiFetch, ReauthRequiredError, NetworkError, setOnUnauthenticated } from './api-client';
import { clearXsrfTokenCacheForTests } from '../auth/csrf';

describe('apiFetch', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    fetchSpy = vi.spyOn(global, 'fetch');
    // Reset the module-level CSRF request-token memo between tests so each
    // test starts without a cached token and can exercise the full handshake.
    clearXsrfTokenCacheForTests();
    // Clear the cookie too (belt-and-suspenders; __Host- prefix requires Secure
    // attribute; vite.config sets the jsdom URL to https://localhost so Secure
    // cookies are accepted).
    document.cookie = '__Host-XSRF=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/; Secure';
  });

  afterEach(() => vi.restoreAllMocks());

  function jsonResponse(body: unknown, init: ResponseInit = {}): Response {
    // The Response constructor rejects a body for 204/205/304; guard here.
    const status = init.status ?? 200;
    const hasBody = body !== null && body !== undefined && status !== 204 && status !== 205;
    return new Response(hasBody ? JSON.stringify(body) : null, {
      ...init,
      headers: hasBody
        ? { 'Content-Type': 'application/json', ...(init.headers ?? {}) }
        : (init.headers ?? {}),
    });
  }

  it('GET request includes credentials but does not run CSRF handshake', async () => {
    fetchSpy.mockResolvedValueOnce(jsonResponse({ ok: true }, { status: 200 }));

    const result = await apiFetch('/api/auth/me');

    expect(result.ok).toBe(true);
    expect(fetchSpy).toHaveBeenCalledTimes(1);
    expect(fetchSpy).toHaveBeenCalledWith(
      '/api/auth/me',
      expect.objectContaining({ credentials: 'include', method: 'GET' }),
    );
  });

  it('POST request runs the CSRF handshake first when no token cached', async () => {
    fetchSpy
      // Handshake call to /api/auth/csrf — returns request token in response header.
      .mockImplementationOnce(async () => {
        return new Response(null, {
          status: 204,
          headers: { 'X-XSRF-TOKEN': 'test-csrf-token' },
        });
      })
      // The actual POST.
      .mockResolvedValueOnce(jsonResponse(null, { status: 204 }));

    await apiFetch('/api/auth/login', { method: 'POST', body: { email: 'x', password: 'y' } });

    expect(fetchSpy).toHaveBeenCalledTimes(2);
    expect(fetchSpy.mock.calls[0][0]).toBe('/api/auth/csrf');
    expect(fetchSpy.mock.calls[1][0]).toBe('/api/auth/login');
    const loginInit = fetchSpy.mock.calls[1][1] as RequestInit;
    expect(loginInit.headers).toEqual(expect.objectContaining({ 'X-XSRF-TOKEN': 'test-csrf-token' }));
  });

  it('CSRF handshake is deduped across concurrent requests', async () => {
    let handshakeCount = 0;
    fetchSpy.mockImplementation(async (url) => {
      if (url === '/api/auth/csrf') {
        handshakeCount++;
        return new Response(null, {
          status: 204,
          headers: { 'X-XSRF-TOKEN': 'test-csrf-token' },
        });
      }
      return jsonResponse(null, { status: 204 });
    });

    await Promise.all([
      apiFetch('/api/auth/login', { method: 'POST', body: {} }),
      apiFetch('/api/auth/login', { method: 'POST', body: {} }),
      apiFetch('/api/auth/login', { method: 'POST', body: {} }),
    ]);

    expect(handshakeCount).toBe(1);
  });

  it('maps 422 with field details into fieldErrors', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (url === '/api/auth/csrf') {
        return new Response(null, {
          status: 204,
          headers: { 'X-XSRF-TOKEN': 'test-csrf-token' },
        });
      }
      return jsonResponse(
        {
          error: {
            code: 'VALIDATION_ERROR',
            message: 'One or more fields are invalid.',
            details: [{ field: 'email', message: 'Required.' }],
          },
        },
        { status: 422 },
      );
    });

    const result = await apiFetch('/api/auth/login', { method: 'POST', body: {} });

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.fieldErrors).toEqual({ email: 'Required.' });
    }
  });

  it('maps 422 with empty details into formError', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (url === '/api/auth/csrf') {
        return new Response(null, {
          status: 204,
          headers: { 'X-XSRF-TOKEN': 'test-csrf-token' },
        });
      }
      return jsonResponse(
        {
          error: { code: 'VALIDATION_ERROR', message: 'Something went wrong.', details: [] },
        },
        { status: 422 },
      );
    });

    const result = await apiFetch('/api/auth/login', { method: 'POST', body: {} });

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.formError).toBe('Something went wrong.');
    }
  });

  it('throws ReauthRequiredError on 401 REAUTH_REQUIRED', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (url === '/api/auth/csrf') {
        return new Response(null, {
          status: 204,
          headers: { 'X-XSRF-TOKEN': 'test-csrf-token' },
        });
      }
      return jsonResponse(
        { error: { code: 'REAUTH_REQUIRED', message: 'Please reauthenticate.' } },
        { status: 401 },
      );
    });

    await expect(
      apiFetch('/api/auth/mfa/disable', { method: 'POST', body: {} }),
    ).rejects.toBeInstanceOf(ReauthRequiredError);
  });

  it('throws NetworkError when fetch rejects', async () => {
    fetchSpy.mockRejectedValueOnce(new TypeError('Failed to fetch'));

    await expect(apiFetch('/api/auth/me')).rejects.toBeInstanceOf(NetworkError);
  });

  describe('setOnUnauthenticated callback', () => {
    afterEach(() => setOnUnauthenticated(null));

    it('fires the handler on a plain 401 from a non-auth-probe URL', async () => {
      const handler = vi.fn();
      setOnUnauthenticated(handler);

      fetchSpy.mockResolvedValueOnce(
        jsonResponse(
          { error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' } },
          { status: 401 },
        ),
      );

      const result = await apiFetch('/api/dashboard/summary');

      expect(result.ok).toBe(false);
      expect(handler).toHaveBeenCalledTimes(1);
    });

    it('does NOT fire the handler when /api/auth/me returns 401 (that is the auth probe itself)', async () => {
      const handler = vi.fn();
      setOnUnauthenticated(handler);

      fetchSpy.mockResolvedValueOnce(
        jsonResponse(
          { error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' } },
          { status: 401 },
        ),
      );

      await apiFetch('/api/auth/me');

      expect(handler).not.toHaveBeenCalled();
    });

    it('does NOT fire the handler when /api/auth/csrf returns 401', async () => {
      const handler = vi.fn();
      setOnUnauthenticated(handler);

      fetchSpy.mockResolvedValueOnce(
        jsonResponse(
          { error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' } },
          { status: 401 },
        ),
      );

      await apiFetch('/api/auth/csrf');

      expect(handler).not.toHaveBeenCalled();
    });

    it('does NOT fire the handler on a 401 REAUTH_REQUIRED (that path throws instead)', async () => {
      const handler = vi.fn();
      setOnUnauthenticated(handler);

      fetchSpy.mockImplementation(async (url) => {
        if (url === '/api/auth/csrf') {
          return new Response(null, {
            status: 204,
            headers: { 'X-XSRF-TOKEN': 'test-csrf-token' },
          });
        }
        return jsonResponse(
          { error: { code: 'REAUTH_REQUIRED', message: 'Please reauthenticate.' } },
          { status: 401 },
        );
      });

      await expect(
        apiFetch('/api/auth/mfa/disable', { method: 'POST', body: {} }),
      ).rejects.toBeInstanceOf(ReauthRequiredError);
      expect(handler).not.toHaveBeenCalled();
    });

    it('does not fire when no handler is registered (null setter)', async () => {
      setOnUnauthenticated(null);

      fetchSpy.mockResolvedValueOnce(
        jsonResponse(
          { error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' } },
          { status: 401 },
        ),
      );

      // Should not throw — just returns the generic failure.
      const result = await apiFetch('/api/dashboard/summary');
      expect(result.ok).toBe(false);
    });
  });
});
