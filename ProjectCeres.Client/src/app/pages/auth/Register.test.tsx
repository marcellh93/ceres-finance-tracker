import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { AuthProvider } from '../../auth/auth-context';
import { Register } from './Register';
import { clearXsrfTokenCacheForTests } from '../../auth/csrf';

function renderRegister() {
  return render(
    <I18nextProvider i18n={i18n}>
      <AuthProvider>
        <MemoryRouter initialEntries={['/register']}>
          <Routes>
            <Route path="/register" element={<Register />} />
            <Route path="/login" element={<div>login page</div>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </I18nextProvider>,
  );
}

describe('Register page', () => {
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

  it('renders email + password + submit + back-to-sign-in', () => {
    renderRegister();
    expect(screen.getByLabelText(/email/i)).toBeDefined();
    expect(screen.getByLabelText(/password/i)).toBeDefined();
    expect(screen.getByRole('button', { name: /create account/i })).toBeDefined();
    expect(screen.getByRole('link', { name: /back to sign in/i })).toBeDefined();
  });

  it('204 replaces form with success block referencing the typed email', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/register') {
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderRegister();
    await user.type(screen.getByLabelText(/email/i), 'foo@example.com');
    await user.type(screen.getByLabelText(/password/i), 'ValidPass!2026');
    await user.click(screen.getByRole('button', { name: /create account/i }));
    await waitFor(() => expect(screen.getByText(/check your inbox/i)).toBeDefined());
    expect(screen.getByText(/foo@example\.com/)).toBeDefined();
  });

  it('422 password policy violation → field error', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/register') {
        return new Response(
          JSON.stringify({
            error: {
              code: 'VALIDATION_ERROR',
              message: 'policy',
              details: [{ field: 'Password', message: 'auth.register.errors.passwordBreached' }],
            },
          }),
          { status: 422, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderRegister();
    await user.type(screen.getByLabelText(/email/i), 'foo@example.com');
    await user.type(screen.getByLabelText(/password/i), 'ValidPass!2026');
    await user.click(screen.getByRole('button', { name: /create account/i }));
    await waitFor(() => expect(screen.getByText(/appeared in a known/i)).toBeDefined());
  });

  it('network error → inline error', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/register') {
        throw new TypeError('network down');
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderRegister();
    await user.type(screen.getByLabelText(/email/i), 'foo@example.com');
    await user.type(screen.getByLabelText(/password/i), 'ValidPass!2026');
    await user.click(screen.getByRole('button', { name: /create account/i }));
    await waitFor(() =>
      expect(screen.getByRole('alert').textContent).toMatch(/couldn't reach/i),
    );
  });

  it('zod: password < 8 chars → field error, no fetch', async () => {
    const user = userEvent.setup();
    renderRegister();
    await user.type(screen.getByLabelText(/email/i), 'foo@example.com');
    await user.type(screen.getByLabelText(/password/i), 'short');
    await user.click(screen.getByRole('button', { name: /create account/i }));
    await waitFor(() => expect(screen.getByText(/must be at least 8/i)).toBeDefined());
    const registerCalls = (fetchSpy.mock.calls as unknown as [string, RequestInit?][]).filter(
      ([url]) => url === '/api/auth/register',
    );
    expect(registerCalls).toHaveLength(0);
  });
});
