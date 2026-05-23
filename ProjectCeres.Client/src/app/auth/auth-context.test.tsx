import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider, useAuth } from './auth-context';
import {
  clearXsrfTokenCacheForTests,
  getCachedXsrfRequestToken,
  setCachedXsrfRequestToken,
} from './csrf';

function StatusProbe() {
  const auth = useAuth();
  return <div data-testid="status">{auth.status}</div>;
}

function UserProbe() {
  const auth = useAuth();
  return <div data-testid="user-email">{auth.user?.email ?? 'none'}</div>;
}

describe('AuthProvider', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    fetchSpy = vi.spyOn(global, 'fetch');
    clearXsrfTokenCacheForTests();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    clearXsrfTokenCacheForTests();
  });

  it('starts in loading status', () => {
    fetchSpy.mockImplementationOnce(() => new Promise(() => {})); // never resolves
    render(
      <AuthProvider>
        <StatusProbe />
      </AuthProvider>,
    );
    expect(screen.getByTestId('status').textContent).toBe('loading');
  });

  it('transitions to authed and exposes user when /api/auth/me returns 200', async () => {
    fetchSpy.mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          userId: '00000000-0000-0000-0000-000000000001',
          email: 'a@b.test',
          twoFactorEnabled: false,
          lastReauthAt: null,
          backupCodesRemaining: 0,
          usedBackupCodeAtLastLogin: false,
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );
    render(
      <AuthProvider>
        <StatusProbe />
        <UserProbe />
      </AuthProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('authed'));
    expect(screen.getByTestId('user-email').textContent).toBe('a@b.test');
  });

  it('transitions to anon when /api/auth/me returns 401 on both initial and retry', async () => {
    // refresh() retries once on non-ok to honor the Remember-Me rotation
    // handshake (see auth-context.tsx:refresh comment). For a genuine logout,
    // both calls return 401 and we drop to anon.
    fetchSpy.mockResolvedValue(
      new Response(
        JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' } }),
        { status: 401, headers: { 'Content-Type': 'application/json' } },
      ),
    );
    render(
      <AuthProvider>
        <StatusProbe />
      </AuthProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('anon'));
  });

  it('logout() clears local user + status to anon on 204 success', async () => {
    // Use mockImplementation (not mockResolvedValueOnce) because apiFetch's
    // CSRF handshake injects an intermediate /api/auth/csrf fetch between
    // /api/auth/me (mount) and /api/auth/logout (button click).
    fetchSpy.mockImplementation(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/auth/me')) return AUTHED_ME_RESPONSE.clone();
      if (url.includes('/api/auth/csrf')) {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'test-token' } });
      }
      if (url.includes('/api/auth/logout')) return new Response(null, { status: 204 });
      throw new Error(`Unexpected fetch URL: ${url}`);
    });
    render(
      <AuthProvider>
        <StatusProbe />
        <UserProbe />
        <LogoutButton />
      </AuthProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('authed'));
    await userEvent.setup().click(screen.getByRole('button', { name: 'logout' }));
    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('anon'));
    expect(screen.getByTestId('user-email').textContent).toBe('none');
  });

  it('logout() clears the cached CSRF request token (server rotates the pair on logout)', async () => {
    // Seed a cached token as if a prior state-changing request had set it.
    setCachedXsrfRequestToken('stale-token-from-previous-session');
    expect(getCachedXsrfRequestToken()).toBe('stale-token-from-previous-session');

    fetchSpy.mockImplementation(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/auth/me')) return AUTHED_ME_RESPONSE.clone();
      if (url.includes('/api/auth/logout')) return new Response(null, { status: 204 });
      throw new Error(`Unexpected fetch URL: ${url}`);
    });
    render(
      <AuthProvider>
        <StatusProbe />
        <LogoutButton />
      </AuthProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('authed'));
    await userEvent.setup().click(screen.getByRole('button', { name: 'logout' }));
    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('anon'));

    // Stale token must be gone — otherwise the next POST (e.g. login) sends
    // the old request token against the server's newly-rotated cookie and
    // gets rejected with 400. This was the regression introduced by 9.1.5.f
    // before the cache-clear was added.
    expect(getCachedXsrfRequestToken()).toBeNull();
  });

  it('drops to anon when a non-auth-probe API call returns 401 mid-session (silent session expiry)', async () => {
    // Mount authed, then simulate a dashboard fetch returning 401 because
    // the server-side session expired silently between requests. The auth
    // context should transition to 'anon' so RequireAuth redirects on the
    // next navigation.
    fetchSpy.mockImplementation(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/auth/me')) return AUTHED_ME_RESPONSE.clone();
      if (url.includes('/api/dashboard/summary')) {
        return new Response(
          JSON.stringify({
            error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' },
          }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      throw new Error(`Unexpected fetch URL: ${url}`);
    });
    function ExpiredFetchProbe() {
      const auth = useAuth();
      return (
        <button
          onClick={async () => {
            // Import inline to avoid a top-of-file cycle with api-client.
            const { apiFetch } = await import('../lib/api-client');
            await apiFetch('/api/dashboard/summary');
          }}
        >
          {auth.status === 'authed' ? 'fetch' : 'anon'}
        </button>
      );
    }
    render(
      <AuthProvider>
        <StatusProbe />
        <ExpiredFetchProbe />
      </AuthProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('authed'));

    await userEvent.setup().click(screen.getByRole('button', { name: 'fetch' }));

    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('anon'));
  });

  it('retries /api/auth/me once on first-call 401 (Remember-Me rotation handshake) and stays authed when retry succeeds', async () => {
    // First /api/auth/me returns 401 — simulates the browser sending a stale
    // session cookie alongside a valid persist cookie, server rotating and
    // returning 401 with Set-Cookie. The browser stores the new cookie. Second
    // /api/auth/me is the retry; it carries the rotated cookie and succeeds.
    // AuthProvider must end in 'authed', not 'anon'.
    let callCount = 0;
    fetchSpy.mockImplementation(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/auth/me')) {
        callCount++;
        if (callCount === 1) {
          return new Response(null, {
            status: 401,
            headers: { 'X-Ceres-Cookie-Rotated': 'true' },
          });
        }
        return AUTHED_ME_RESPONSE.clone();
      }
      throw new Error(`Unexpected fetch URL: ${url}`);
    });
    render(
      <AuthProvider>
        <StatusProbe />
      </AuthProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('authed'));
    expect(callCount).toBe(2);
  });

  it('drops to anon when both /api/auth/me calls return 401 (genuine session expiry)', async () => {
    // Both the first call AND the retry return 401 — no rotation happened, the
    // user is genuinely logged out. AuthProvider must end in 'anon'.
    let callCount = 0;
    fetchSpy.mockImplementation(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/auth/me')) {
        callCount++;
        return new Response(null, { status: 401 });
      }
      throw new Error(`Unexpected fetch URL: ${url}`);
    });
    render(
      <AuthProvider>
        <StatusProbe />
      </AuthProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('anon'));
    expect(callCount).toBe(2);
  });

  it('does NOT drop to anon when /api/auth/me itself returns 401 (handled by refresh, not the silent-expiry seam)', async () => {
    // Belt-and-suspenders: the silent-expiry seam exempts /api/auth/me so it
    // doesn't double-fire alongside refresh()'s own anon transition. This
    // test verifies the existing transition still works through the original
    // path (refresh sees the 401, sets anon) without help from the seam.
    // refresh() retries once; mockResolvedValue (unconditional) covers both calls.
    fetchSpy.mockResolvedValue(
      new Response(
        JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' } }),
        { status: 401, headers: { 'Content-Type': 'application/json' } },
      ),
    );
    render(
      <AuthProvider>
        <StatusProbe />
      </AuthProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('anon'));
  });

  it('logout() still clears local state even when server returns 401 (server already lost the session)', async () => {
    fetchSpy.mockImplementation(async (input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/auth/me')) return AUTHED_ME_RESPONSE.clone();
      if (url.includes('/api/auth/csrf')) {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'test-token' } });
      }
      if (url.includes('/api/auth/logout')) {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      throw new Error(`Unexpected fetch URL: ${url}`);
    });
    render(
      <AuthProvider>
        <StatusProbe />
        <LogoutButton />
      </AuthProvider>,
    );
    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('authed'));
    await userEvent.setup().click(screen.getByRole('button', { name: 'logout' }));
    await waitFor(() => expect(screen.getByTestId('status').textContent).toBe('anon'));
  });
});

const AUTHED_ME_RESPONSE = new Response(
  JSON.stringify({
    userId: '00000000-0000-0000-0000-000000000001',
    email: 'a@b.test',
    twoFactorEnabled: false,
    lastReauthAt: null,
    backupCodesRemaining: 0,
    usedBackupCodeAtLastLogin: false,
  }),
  { status: 200, headers: { 'Content-Type': 'application/json' } },
);

function LogoutButton() {
  const { logout } = useAuth();
  return <button onClick={() => void logout()}>logout</button>;
}
