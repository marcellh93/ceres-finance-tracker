import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
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

// Probe component: renders the login heading + raw search string so the
// test can assert that ?redirect=%2Fdashboard arrives correctly encoded.
// A regression dropping encodeURIComponent or renaming the query param
// would produce a different search string and fail the assertion below.
function LoginProbe() {
  const location = useLocation();
  return <div>login page{location.search}</div>;
}

describe('RequireAuth — anonymous redirect', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    fetchSpy = vi.spyOn(global, 'fetch');
    // refresh() in auth-context retries /api/auth/me once on non-ok to honor
    // the Remember-Me rotation handshake (see auth-context.tsx:refresh
    // comment). Use mockResolvedValue (unconditional) so both calls return 401.
    fetchSpy.mockResolvedValue(
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
            <Route path="/login" element={<LoginProbe />} />
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
    await waitFor(() =>
      expect(screen.getByText('login page?redirect=%2Fdashboard')).toBeDefined(),
    );
  });
});
