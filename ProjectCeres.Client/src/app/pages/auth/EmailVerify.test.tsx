import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { AuthProvider } from '../../auth/auth-context';
import { EmailVerify } from './EmailVerify';
import { clearXsrfTokenCacheForTests } from '../../auth/csrf';

function renderAt(initialPath: string) {
  return render(
    <I18nextProvider i18n={i18n}>
      <AuthProvider>
        <MemoryRouter initialEntries={[initialPath]}>
          <Routes>
            <Route path="/email-verify" element={<EmailVerify />} />
            <Route path="/login" element={<div>login page</div>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </I18nextProvider>,
  );
}

describe('EmailVerify page', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

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

  it('no hash → invalid-link error block + resend button', async () => {
    renderAt('/email-verify');
    await waitFor(() =>
      expect(screen.getByText(/this verification link is invalid/i)).toBeDefined(),
    );
    expect(screen.getByRole('button', { name: /resend verification email/i })).toBeDefined();
  });

  it('token in hash → fires POST /api/auth/email/verify with that token', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/email/verify') {
        const body = JSON.parse((init?.body as string) ?? '{}');
        captured = body.token;
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    renderAt('/email-verify#token=abc123');
    await waitFor(() => expect(screen.getByText(/email verified/i)).toBeDefined());
    expect(captured).toBe('abc123');
  });

  it('204 → success block + sign-in link', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/email/verify') {
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    renderAt('/email-verify#token=abc');
    await waitFor(() => expect(screen.getByText(/email verified/i)).toBeDefined());
    expect(screen.getByRole('link', { name: /go to sign in/i })).toBeDefined();
  });

  it('401 INVALID_VERIFICATION_TOKEN → invalid-link error block + resend', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/email/verify') {
        return new Response(
          JSON.stringify({ error: { code: 'INVALID_VERIFICATION_TOKEN', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    renderAt('/email-verify#token=bad');
    await waitFor(() =>
      expect(screen.getByText(/this verification link is invalid/i)).toBeDefined(),
    );
    expect(screen.getByRole('button', { name: /resend verification email/i })).toBeDefined();
  });

  it('resend → POST /api/auth/email/verify/resend → success acknowledgement', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/email/verify') {
        return new Response(
          JSON.stringify({ error: { code: 'INVALID_VERIFICATION_TOKEN', message: '' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/email/verify/resend') {
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderAt('/email-verify#token=bad');
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /resend verification email/i })).toBeDefined(),
    );
    await user.click(screen.getByRole('button', { name: /resend verification email/i }));
    await user.type(screen.getByLabelText(/email/i), 'foo@example.com');
    await user.click(screen.getByRole('button', { name: /send link/i }));
    await waitFor(() =>
      expect(screen.getByText(/we've sent a new verification link/i)).toBeDefined(),
    );
  });
});
