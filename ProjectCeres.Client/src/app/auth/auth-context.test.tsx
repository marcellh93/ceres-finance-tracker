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

  it('transitions to anon when /api/auth/me returns 401', async () => {
    fetchSpy.mockResolvedValueOnce(
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
