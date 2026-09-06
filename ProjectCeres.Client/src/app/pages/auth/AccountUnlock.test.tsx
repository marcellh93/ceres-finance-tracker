import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { AuthProvider } from '../../auth/auth-context';
import { AccountUnlock } from './AccountUnlock';
import { clearXsrfTokenCacheForTests } from '../../auth/csrf';

function LoginStub() {
  const location = useLocation();
  return <div>login page (search={location.search})</div>;
}

function renderAt(initialPath: string) {
  return render(
    <I18nextProvider i18n={i18n}>
      <AuthProvider>
        <MemoryRouter initialEntries={[initialPath]}>
          <Routes>
            <Route path="/account/unlock" element={<AccountUnlock />} />
            <Route path="/login" element={<LoginStub />} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </I18nextProvider>,
  );
}

describe('AccountUnlock page', () => {
  let fetchSpy: MockInstance<typeof fetch>;

  beforeEach(() => {
    fetchSpy = vi.spyOn(global, 'fetch');
    clearXsrfTokenCacheForTests();
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, {
          status: 204,
          headers: { 'X-XSRF-TOKEN': 'tok' },
        });
      }
      return new Response(null, { status: 204 });
    });
    document.cookie = '__Host-XSRF=test; path=/';
  });

  afterEach(() => vi.restoreAllMocks());

  it('U1: renders unlock button when token in hash; does NOT auto-submit', async () => {
    let called = false;
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/lockout-unlock') {
        called = true;
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    renderAt('/account/unlock#token=abc');
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /unlock account/i })).toBeDefined(),
    );
    // Give the component a tick to (incorrectly) auto-submit; assert it didn't.
    await new Promise((r) => setTimeout(r, 50));
    expect(called).toBe(false);
  });

  it('U2: no token in hash → renders invalid-link block', async () => {
    renderAt('/account/unlock');
    await waitFor(() =>
      expect(screen.getByText(/this unlock link is invalid/i)).toBeDefined(),
    );
    expect(screen.getByRole('link', { name: /back to sign in/i })).toBeDefined();
  });

  it('U3: click → POSTs /api/auth/lockout-unlock with token', async () => {
    let captured: string | undefined;
    fetchSpy.mockImplementation(async (url, init) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/lockout-unlock') {
        const body = JSON.parse((init?.body as string) ?? '{}');
        captured = body.token;
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderAt('/account/unlock#token=abc');
    await user.click(await screen.findByRole('button', { name: /unlock account/i }));
    await waitFor(() => expect(captured).toBe('abc'));
  });

  it('U4: 204 → navigates to /login?unlocked=1', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/lockout-unlock') {
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderAt('/account/unlock#token=abc');
    await user.click(await screen.findByRole('button', { name: /unlock account/i }));
    await waitFor(() => expect(screen.getByText(/login page/i)).toBeDefined());
    expect(screen.getByText(/login page/i).textContent).toContain('unlocked=1');
  });

  it('U5: 401 → invalid-link block', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/lockout-unlock') {
        return new Response(
          JSON.stringify({ error: { code: 'INVALID_LOCKOUT_UNLOCK_TOKEN', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderAt('/account/unlock#token=bad');
    await user.click(await screen.findByRole('button', { name: /unlock account/i }));
    await waitFor(() =>
      expect(screen.getByText(/this unlock link is invalid/i)).toBeDefined(),
    );
  });

  it('U6: network error → retry block; retry re-submits', async () => {
    let firstAttempt = true;
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/lockout-unlock') {
        if (firstAttempt) {
          firstAttempt = false;
          throw new TypeError('Network request failed');
        }
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderAt('/account/unlock#token=abc');
    await user.click(await screen.findByRole('button', { name: /unlock account/i }));
    await waitFor(() =>
      expect(screen.getByText(/couldn't reach/i)).toBeDefined(),
    );
    await user.click(screen.getByRole('button', { name: /try again/i }));
    await waitFor(() => expect(screen.getByText(/login page/i)).toBeDefined());
    expect(screen.getByText(/login page/i).textContent).toContain('unlocked=1');
  });
});
