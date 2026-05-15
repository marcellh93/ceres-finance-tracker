import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider, useAuth } from './auth-context';

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
  });

  afterEach(() => vi.restoreAllMocks());

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
});
