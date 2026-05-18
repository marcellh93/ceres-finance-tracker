import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('sonner', async () => {
  const actual = await vi.importActual<typeof import('sonner')>('sonner');
  return { ...actual, toast: vi.fn() };
});
import { toast } from 'sonner';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { AuthProvider } from '../../auth/auth-context';
import { Login } from './Login';
import {
  clearXsrfTokenCacheForTests,
  getCachedXsrfRequestToken,
  setCachedXsrfRequestToken,
} from '../../auth/csrf';

function renderLogin(initialPath = '/login') {
  return render(
    <I18nextProvider i18n={i18n}>
      <AuthProvider>
        <MemoryRouter initialEntries={[initialPath]}>
          <Routes>
            <Route path="/login" element={<Login />} />
            <Route path="/" element={<div>dashboard</div>} />
            <Route path="/login/totp" element={<div>totp step</div>} />
            <Route path="/account/unlock" element={<div>unlock page</div>} />
            <Route path="/password-reset" element={<div>password reset request</div>} />
            <Route path="/register" element={<div>register page</div>} />
            <Route path="/movements" element={<div>movements</div>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </I18nextProvider>,
  );
}

describe('Login page', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    fetchSpy = vi.spyOn(global, 'fetch');
    // Reset the module-level CSRF request-token memo between tests to avoid
    // cross-test pollution (the memo persists for the module lifetime).
    clearXsrfTokenCacheForTests();
    // /api/auth/me returns 401 (anonymous) by default for these tests.
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      // /api/auth/csrf returns the request token in the response header.
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, {
          status: 204,
          headers: { 'X-XSRF-TOKEN': 'test-csrf-token' },
        });
      }
      return new Response(null, { status: 204 });
    });
    document.cookie = '__Host-XSRF=test; path=/';
  });

  afterEach(() => vi.restoreAllMocks());

  it('renders the form with email + password + remember-me + submit', () => {
    renderLogin();
    expect(screen.getByLabelText(/email/i)).toBeDefined();
    expect(screen.getByLabelText(/password/i)).toBeDefined();
    expect(screen.getByLabelText(/remember me/i)).toBeDefined();
    expect(screen.getByRole('button', { name: /sign in/i })).toBeDefined();
  });

  it('renders the forgot-password and create-account links', () => {
    renderLogin();
    expect(screen.getByRole('link', { name: /forgot password/i })).toBeDefined();
    expect(screen.getByRole('link', { name: /create an account/i })).toBeDefined();
  });

  it('submits via Enter key from the password field', async () => {
    const user = userEvent.setup();
    renderLogin();
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.type(screen.getByLabelText(/password/i), 'pw{Enter}');

    // After /api/auth/me's initial 401, the next fetch is to /api/auth/login.
    await waitFor(() =>
      expect(fetchSpy).toHaveBeenCalledWith(
        '/api/auth/login',
        expect.objectContaining({ method: 'POST' }),
      ),
    );
  });

  it('on success without TOTP redirects to / regardless of TOTP status', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/login') {
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLogin();
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.type(screen.getByLabelText(/password/i), 'pw');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(screen.getByText('dashboard')).toBeDefined());
  });

  it('on requiresTotp:true redirects to /login/totp', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/login') {
        return new Response(JSON.stringify({ requiresTotp: true }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLogin();
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.type(screen.getByLabelText(/password/i), 'pw');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(screen.getByText('totp step')).toBeDefined());
  });

  it('renders an inline field error on INVALID_CREDENTIALS', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/login') {
        return new Response(
          JSON.stringify({ error: { code: 'INVALID_CREDENTIALS', message: 'Invalid email or password.' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLogin();
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.type(screen.getByLabelText(/password/i), 'pw');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(screen.getByText(/email or password is incorrect/i)).toBeDefined());
  });

  it('redirects to /account/unlock on ACCOUNT_LOCKED_OUT', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/login') {
        return new Response(
          JSON.stringify({ error: { code: 'ACCOUNT_LOCKED_OUT', message: 'Locked.' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLogin();
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.type(screen.getByLabelText(/password/i), 'pw');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(screen.getByText('unlock page')).toBeDefined());
  });

  it('renders the resend-verification link on EMAIL_NOT_CONFIRMED', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/login') {
        return new Response(
          JSON.stringify({ error: { code: 'EMAIL_NOT_CONFIRMED', message: 'Verify first.' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLogin();
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.type(screen.getByLabelText(/password/i), 'pw');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /resend verification email/i })).toBeDefined(),
    );
  });

  it('fires the password-reset toast when /login?reset=1', async () => {
    render(
      <I18nextProvider i18n={i18n}>
        <AuthProvider>
          <MemoryRouter initialEntries={['/login?reset=1']}>
            <Routes>
              <Route path="/login" element={<Login />} />
            </Routes>
          </MemoryRouter>
        </AuthProvider>
      </I18nextProvider>,
    );
    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(
        expect.stringMatching(/password reset/i),
      ),
    );
  });

  it('fires the expired-sign-in toast when /login?expired=1', async () => {
    render(
      <I18nextProvider i18n={i18n}>
        <AuthProvider>
          <MemoryRouter initialEntries={['/login?expired=1']}>
            <Routes>
              <Route path="/login" element={<Login />} />
            </Routes>
          </MemoryRouter>
        </AuthProvider>
      </I18nextProvider>,
    );
    await waitFor(() =>
      expect(toast).toHaveBeenCalledWith(
        expect.stringMatching(/your sign-in expired/i),
      ),
    );
  });

  it('clears the cached CSRF request token after a successful login', async () => {
    // Server rotates the CSRF pair on login. If the SPA keeps the
    // pre-login request-token cached, the next state-changing call sends
    // it against the freshly-rotated cookie and the server rejects with
    // 400. Regression test for the /app/security "first click 400s,
    // refresh fixes it" bug surfaced 2026-05-18.
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'pre-login-token' } });
      }
      if (typeof url === 'string' && url === '/api/auth/login') {
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    // Pre-seed the cache to simulate "we already did a handshake before login".
    setCachedXsrfRequestToken('pre-login-token');
    expect(getCachedXsrfRequestToken()).toBe('pre-login-token');

    const user = userEvent.setup();
    renderLogin();
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.type(screen.getByLabelText(/password/i), 'pw');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(screen.getByText('dashboard')).toBeDefined());
    expect(getCachedXsrfRequestToken()).toBeNull();
  });

  it('clears the cached CSRF request token before redirecting to /login/totp', async () => {
    // Same rotation happens when the server returns requiresTotp: true
    // (it sets the Identity.TwoFactorUserId cookie + calls GetAndStoreTokens).
    // The TOTP page's first submit must re-handshake.
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'pre-login-token' } });
      }
      if (typeof url === 'string' && url === '/api/auth/login') {
        return new Response(JSON.stringify({ requiresTotp: true }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response(null, { status: 204 });
    });
    setCachedXsrfRequestToken('pre-login-token');

    const user = userEvent.setup();
    renderLogin();
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.type(screen.getByLabelText(/password/i), 'pw');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(screen.getByText('totp step')).toBeDefined());
    expect(getCachedXsrfRequestToken()).toBeNull();
  });

  it('honours the redirect query param on success', async () => {
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/login') {
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLogin('/login?redirect=%2Fmovements');
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.type(screen.getByLabelText(/password/i), 'pw');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(screen.getByText('movements')).toBeDefined());
  });
});
