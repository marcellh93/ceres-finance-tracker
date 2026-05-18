import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { App } from './App';
import { AuthProvider } from './auth/auth-context';
import { ThemeProvider } from './theme/theme-context';

// Mock /api/auth/me to return an authenticated user so RequireAuth
// resolves to 'authed' and renders protected children. All App.test.tsx
// tests exercise the route table, not auth behaviour — auth is covered
// by RequireAuth.test.tsx and auth-context.test.tsx.
//
// mockResolvedValueOnce (not mockResolvedValue): the persistent form was
// silently answering every subsequent fetch (provider badge counts, page
// data loads) with the /me shape, masking the real fetch contract and
// making tests fragile as more page-level fetches arrive.
function mockAuthedMe() {
  vi.spyOn(global, 'fetch').mockResolvedValueOnce(
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
}

function renderApp(path: string) {
  return render(
    <ThemeProvider>
      <AuthProvider>
        <MemoryRouter initialEntries={[path]}>
          <App />
        </MemoryRouter>
      </AuthProvider>
    </ThemeProvider>,
  );
}

const routes: Array<{ path: string; expectedHeading: string }> = [
  { path: '/login',          expectedHeading: 'Sign in' },
  { path: '/password-reset', expectedHeading: 'Reset your password' },
  { path: '/register',       expectedHeading: 'Account creation is coming soon' },
  { path: '/',             expectedHeading: 'Dashboard' },
  { path: '/movements',    expectedHeading: 'Movements' },
  { path: '/review',       expectedHeading: 'Review' },
  { path: '/accounts',     expectedHeading: 'Accounts' },
  { path: '/categories',   expectedHeading: 'Categories' },
  { path: '/budgets',      expectedHeading: 'Budgets' },
  { path: '/import',       expectedHeading: 'Import' },
  { path: '/reports/net-worth-over-time', expectedHeading: 'Net Worth Over Time' },
  { path: '/settings',     expectedHeading: 'Settings' },
  { path: '/support',      expectedHeading: 'Support' },
  { path: '/profile',      expectedHeading: 'Profile' },
  { path: '/security',     expectedHeading: 'Security' },
  { path: '/this-route-does-not-exist', expectedHeading: 'Page not found' },
];

describe('App routes', () => {
  afterEach(() => vi.restoreAllMocks());

  for (const { path, expectedHeading } of routes) {
    it(`renders the heading "${expectedHeading}" at ${path}`, async () => {
      mockAuthedMe();
      renderApp(path);
      await waitFor(() =>
        expect(screen.getByRole('heading', { level: 1, name: expectedHeading })).toBeDefined()
      );
    });
  }

  it('renders recurring list page at /recurring', async () => {
    // First mock is /api/auth/me (authed); subsequent calls are the recurring fetch.
    vi.spyOn(global, 'fetch')
      .mockResolvedValueOnce(
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
      )
      .mockResolvedValue(new Response(JSON.stringify([]), { status: 200, headers: { 'Content-Type': 'application/json' } }));

    renderApp('/recurring');
    await waitFor(() =>
      expect(screen.getByRole('heading', { name: 'Recurring transactions' })).toBeInTheDocument()
    );
  });

  it('renders the Movement Create page at /movements/new?type=transaction', async () => {
    mockAuthedMe();
    renderApp('/movements/new?type=transaction');
    // MovementCreate renders the form when ?type= is set; Save is the form's primary action.
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /save/i })).toBeDefined()
    );
  });

  it('renders the Movement Edit page at /movements/:id/edit', async () => {
    mockAuthedMe();
    renderApp('/movements/some-id/edit');
    // MovementEdit fetches the discriminator on mount; the loading state renders first
    await waitFor(() =>
      expect(screen.getByText(/loading…/i)).toBeDefined()
    );
  });
});

describe('App routing structure', () => {
  afterEach(() => vi.restoreAllMocks());

  it('still routes existing protected pages through RequireAuth', async () => {
    mockAuthedMe();
    renderApp('/');
    // RequireAuth now reads useAuth(); it renders null while loading, then
    // renders children once authed. Wait for the heading.
    await waitFor(() =>
      expect(screen.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeDefined()
    );
  });

  it('mounts sonner Toaster at the App root so AuthLayout pages get toasts', async () => {
    // Regression for 2026-05-18: <Toaster> was previously mounted inside
    // AppLayout only, which meant /login + /login/totp + /password-reset toast
    // calls (auth pages on AuthLayout) silently no-op'd. Surfaced when the
    // `?expired=1` toast on /login didn't render after LoginTotp navigated
    // there. The fix mounts <Toaster> once at the App root so BOTH layouts
    // inherit it.
    //
    // sonner renders <Toaster> as a section carrying aria-label like
    // "Notifications alt+T" (the label includes sonner's keyboard shortcut).
    // We assert that section is in document.body after rendering any auth route.
    vi.spyOn(global, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
        { status: 401, headers: { 'Content-Type': 'application/json' } },
      ),
    );
    renderApp('/login');
    await waitFor(() =>
      expect(document.body.querySelector('[aria-label^="Notifications"]')).not.toBeNull(),
    );
  });
});
