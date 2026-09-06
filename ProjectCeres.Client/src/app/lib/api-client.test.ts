import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import { apiFetch, ReauthRequiredError, NetworkError, setOnUnauthenticated } from './api-client';
import { clearXsrfTokenCacheForTests } from '../auth/csrf';

describe('apiFetch', () => {
  let fetchSpy: MockInstance<typeof fetch>;

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
    // ApiFailure is a discriminated union; fieldErrors lives only on the field-errors
    // member. Assert the discriminant is present FIRST (so a regressed shape fails here,
    // not silently skips the check), then the assertion below type-checks.
    expect(result.ok === false && 'fieldErrors' in result).toBe(true);
    if (!result.ok && 'fieldErrors' in result) {
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
    // See the fieldErrors test above: assert the discriminant first so a regressed
    // shape fails here rather than skipping the narrowed assertion.
    expect(result.ok === false && 'formError' in result).toBe(true);
    if (!result.ok && 'formError' in result) {
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

    it.each([
      '/api/auth/email-change/confirm',
      '/api/auth/email-change/revoke',
      '/api/auth/email/verify',
      '/api/auth/lockout-unlock',
    ])('does NOT sign the user out when %s rejects a bad token', async (url) => {
      // These are [AllowAnonymous] token endpoints: a 401 means the token in the
      // emailed LINK is bad, not that the caller's session died. Before this
      // exemption, a signed-in user clicking a stale revoke link from their own
      // inbox was flipped to 'anon' and bounced to /login on the next navigation.
      // Found by the Stage 12.8 security review.
      const handler = vi.fn();
      setOnUnauthenticated(handler);

      // A POST would burn this mock on the CSRF handshake and the assertion would
      // pass for the wrong reason — the 401 under test would never be reached.
      // (That is exactly what the first draft of this test did.) GET goes straight
      // through, and the 401 branch under test does not depend on the method.
      fetchSpy.mockResolvedValueOnce(
        jsonResponse(
          { error: { code: 'INVALID_EMAIL_CHANGE_TOKEN', message: 'Invalid or expired.' } },
          { status: 401 },
        ),
      );

      await apiFetch(url).catch(() => {});

      expect(handler).not.toHaveBeenCalled();
    });

    it.each([
      ['/api/auth/email-change/pending', '[Authorize] — a 401 here IS a dead session'],
      ['/api/auth/email-change/request', '[RequireRecentAuth] — same'],
      // Note this one cannot actually 401 — EmailVerificationController.cs:42 returns
      // 204 on every branch (the anti-enumeration property) and 429 when limited. It
      // is here purely as the MATCH control: it sits under an exempted prefix, so it
      // proves the match is exact-or-query-string rather than a bare startsWith. If it
      // ever gains a 401, revisit whether it belongs in TOKEN_AUTH_URLS.
      ['/api/auth/email/verify/resend', 'sits under an exempted prefix; must not over-match'],
    ])('STILL signs the user out on 401 from %s (%s)', async (url) => {
      // The dangerous half of TOKEN_AUTH_URLS: an exemption that over-matched
      // would suppress a genuine session expiry and leave the SPA believing the
      // user is signed in. These three sit next to exempted routes and must keep
      // the old behaviour. Note the match is `=== u || startsWith(u + '?')`, not a
      // bare prefix — which is what keeps /verify/resend out of /verify's exemption.
      const handler = vi.fn();
      setOnUnauthenticated(handler);

      fetchSpy.mockResolvedValueOnce(
        jsonResponse(
          { error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' } },
          { status: 401 },
        ),
      );

      await apiFetch(url).catch(() => {});

      expect(handler).toHaveBeenCalled();
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

    it('transparently retries on 401 with X-Ceres-Cookie-Rotated (Remember-Me rotation handshake) and returns the retry result', async () => {
      const handler = vi.fn();
      setOnUnauthenticated(handler);

      // First call: PersistentCookieRotationMiddleware returned 401 with the
      // rotation marker (fresh cookies set via Set-Cookie). Browser stores them.
      // Second call: the retry carries the new cookies and succeeds with 200.
      // apiFetch must transparently retry and report the retry's success, NOT
      // fire the unauth handler.
      fetchSpy
        .mockResolvedValueOnce(
          new Response(
            JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' } }),
            {
              status: 401,
              headers: {
                'Content-Type': 'application/json',
                'X-Ceres-Cookie-Rotated': 'true',
              },
            },
          ),
        )
        .mockResolvedValueOnce(jsonResponse({ result: 'ok' }, { status: 200 }));

      const result = await apiFetch<{ result: string }>('/api/dashboard/summary');

      expect(result.ok).toBe(true);
      if (result.ok) expect(result.data).toEqual({ result: 'ok' });
      expect(fetchSpy).toHaveBeenCalledTimes(2);
      expect(handler).not.toHaveBeenCalled();
    });

    it('returns the second 401 (no further retry) when the retry itself also 401s', async () => {
      const handler = vi.fn();
      setOnUnauthenticated(handler);

      // Defense against retry loops: even if the second response also 401s
      // (with or without the rotation header), apiFetch must not retry again.
      // Two fetch calls total — first plus one retry — and the unauth handler
      // fires for the FINAL 401 if it lacks the rotation marker.
      fetchSpy
        .mockResolvedValueOnce(
          new Response(null, {
            status: 401,
            headers: { 'X-Ceres-Cookie-Rotated': 'true' },
          }),
        )
        .mockResolvedValueOnce(
          new Response(
            JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' } }),
            { status: 401, headers: { 'Content-Type': 'application/json' } },
          ),
        );

      const result = await apiFetch('/api/dashboard/summary');

      expect(result.ok).toBe(false);
      expect(fetchSpy).toHaveBeenCalledTimes(2);
      expect(handler).toHaveBeenCalledTimes(1);
    });
  });
});
