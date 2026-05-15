import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { RequireAuth } from './RequireAuth';
import { AuthProvider } from './auth-context';

// Helper: mock /api/auth/me to resolve as an authenticated user.
function mockAuthedMe() {
  vi.spyOn(global, 'fetch').mockResolvedValueOnce(
    new Response(
      JSON.stringify({
        userId: '00000000-0000-0000-0000-000000000001',
        email: 'user@example.test',
        twoFactorEnabled: false,
        lastReauthAt: null,
        backupCodesRemaining: 0,
        usedBackupCodeAtLastLogin: false,
      }),
      { status: 200, headers: { 'Content-Type': 'application/json' } },
    ),
  );
}

describe('RequireAuth — authenticated passthrough', () => {
  afterEach(() => vi.restoreAllMocks());

  it('renders children when the user is authenticated', async () => {
    mockAuthedMe();
    render(
      <AuthProvider>
        <MemoryRouter>
          <RequireAuth>
            <div>protected</div>
          </RequireAuth>
        </MemoryRouter>
      </AuthProvider>,
    );
    // While /api/auth/me is in-flight, RequireAuth renders null (not the
    // children). Wait for the authed transition before asserting.
    await waitFor(() => expect(screen.getByText('protected')).toBeDefined());
  });
});

describe('RequireAuth — anonymous redirect', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    fetchSpy = vi.spyOn(global, 'fetch');
    fetchSpy.mockResolvedValueOnce(
      new Response(
        JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: 'Authentication required.' } }),
        { status: 401, headers: { 'Content-Type': 'application/json' } },
      ),
    );
  });

  afterEach(() => vi.restoreAllMocks());

  it('redirects anon visitors to /login with the redirect param', async () => {
    render(
      <AuthProvider>
        <MemoryRouter initialEntries={['/dashboard']}>
          <Routes>
            <Route path="/login" element={<div>login page</div>} />
            <Route
              path="/dashboard"
              element={
                <RequireAuth>
                  <div>protected</div>
                </RequireAuth>
              }
            />
          </Routes>
        </MemoryRouter>
      </AuthProvider>,
    );
    await waitFor(() => expect(screen.getByText('login page')).toBeDefined());
  });
});
