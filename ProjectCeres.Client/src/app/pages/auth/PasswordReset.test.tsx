import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { AuthProvider } from '../../auth/auth-context';
import { PasswordReset } from './PasswordReset';
import { clearXsrfTokenCacheForTests } from '../../auth/csrf';

function renderAt(initialPath: string) {
  return render(
    <I18nextProvider i18n={i18n}>
      <AuthProvider>
        <MemoryRouter initialEntries={[initialPath]}>
          <Routes>
            <Route path="/password-reset" element={<PasswordReset />} />
            <Route path="/login" element={<div>login</div>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </I18nextProvider>,
  );
}

describe('PasswordReset page', () => {
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

  it('no hash → renders request form (email + submit + back link)', () => {
    renderAt('/password-reset');
    expect(screen.getByLabelText(/email/i)).toBeDefined();
    expect(screen.getByRole('button', { name: /send reset link/i })).toBeDefined();
    expect(screen.getByRole('link', { name: /back to sign in/i })).toBeDefined();
  });

  it('empty #token= → treats as request mode', () => {
    renderAt('/password-reset#token=');
    expect(screen.getByLabelText(/email/i)).toBeDefined();
  });

  it('valid hash → renders confirm form (new + confirm password)', () => {
    renderAt('/password-reset#token=abc123');
    expect(screen.getByLabelText(/^new password$/i)).toBeDefined();
    expect(screen.getByLabelText(/confirm new password/i)).toBeDefined();
  });

  it('request 204 replaces form with success block', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/password-reset/request') {
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderAt('/password-reset');
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.click(screen.getByRole('button', { name: /send reset link/i }));
    await waitFor(() => expect(screen.getByText(/check your inbox/i)).toBeDefined());
  });

  it('request 429 surfaces no-countdown variant', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/password-reset/request') {
        return new Response(
          JSON.stringify({ error: { code: 'RATE_LIMITED', message: 'no' } }),
          { status: 429, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderAt('/password-reset');
    await user.type(screen.getByLabelText(/email/i), 'a@b.test');
    await user.click(screen.getByRole('button', { name: /send reset link/i }));
    await waitFor(() =>
      expect(screen.getByRole('alert').textContent).toMatch(/too many requests/i),
    );
  });

  it('confirm: mismatched passwords show field error and do not fetch', async () => {
    const user = userEvent.setup();
    renderAt('/password-reset#token=abc');
    await user.type(screen.getByLabelText(/^new password$/i), 'longenough1');
    await user.type(screen.getByLabelText(/confirm new password/i), 'different1');
    await user.click(screen.getByRole('button', { name: /reset password/i }));
    await waitFor(() => expect(screen.getByText(/passwords do not match/i)).toBeDefined());
    const submitCalls = (fetchSpy.mock.calls as unknown as [string, RequestInit?][]).filter(
      ([url]) => url === '/api/auth/password-reset/confirm',
    );
    expect(submitCalls).toHaveLength(0);
  });

  it('confirm 204 navigates to /login?reset=1', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/password-reset/confirm') {
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderAt('/password-reset#token=abc');
    await user.type(screen.getByLabelText(/^new password$/i), 'longenough1');
    await user.type(screen.getByLabelText(/confirm new password/i), 'longenough1');
    await user.click(screen.getByRole('button', { name: /reset password/i }));
    await waitFor(() => expect(screen.getByText('login')).toBeDefined());
  });

  it('confirm 200 requiresTotp → reveals OTP cells; second submit posts all three fields', async () => {
    let callCount = 0;
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
      if (typeof url === 'string' && url === '/api/auth/password-reset/confirm') {
        callCount += 1;
        if (callCount === 1) {
          return new Response(JSON.stringify({ requiresTotp: true }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          });
        }
        const body = JSON.parse((init?.body as string) ?? '{}');
        if (body.totpCode === '123456') return new Response(null, { status: 204 });
        return new Response(null, { status: 401 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderAt('/password-reset#token=abc');
    await user.type(screen.getByLabelText(/^new password$/i), 'longenough1');
    await user.type(screen.getByLabelText(/confirm new password/i), 'longenough1');
    await user.click(screen.getByRole('button', { name: /reset password/i }));
    await waitFor(() => expect(screen.getByLabelText(/verification code/i)).toBeDefined());
    await user.type(screen.getByLabelText(/verification code/i), '123456');
    await user.click(screen.getByRole('button', { name: /reset password/i }));
    await waitFor(() => expect(screen.getByText('login')).toBeDefined());
  });

  it('confirm 401 INVALID_RESET_TOKEN → invalid-token block with request-new-link', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/password-reset/confirm') {
        return new Response(
          JSON.stringify({ error: { code: 'INVALID_RESET_TOKEN', message: 'no' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderAt('/password-reset#token=abc');
    await user.type(screen.getByLabelText(/^new password$/i), 'longenough1');
    await user.type(screen.getByLabelText(/confirm new password/i), 'longenough1');
    await user.click(screen.getByRole('button', { name: /reset password/i }));
    await waitFor(() => expect(screen.getByRole('alert').textContent).toMatch(/invalid or has expired/i));
    expect(screen.getByRole('link', { name: /request a new link/i })).toBeDefined();
  });

  it('confirm 401 INVALID_MFA_CODE → inline error on OTP, password fields preserved', async () => {
    let callCount = 0;
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
      if (typeof url === 'string' && url === '/api/auth/password-reset/confirm') {
        callCount += 1;
        if (callCount === 1) {
          return new Response(JSON.stringify({ requiresTotp: true }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          });
        }
        return new Response(
          JSON.stringify({ error: { code: 'INVALID_MFA_CODE', message: 'no' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderAt('/password-reset#token=abc');
    await user.type(screen.getByLabelText(/^new password$/i), 'longenough1');
    await user.type(screen.getByLabelText(/confirm new password/i), 'longenough1');
    await user.click(screen.getByRole('button', { name: /reset password/i }));
    await waitFor(() => expect(screen.getByLabelText(/verification code/i)).toBeDefined());
    await user.type(screen.getByLabelText(/verification code/i), '000000');
    await user.click(screen.getByRole('button', { name: /reset password/i }));
    await waitFor(() => expect(screen.getByRole('alert').textContent).toMatch(/invalid verification code/i));
    expect((screen.getByLabelText(/^new password$/i) as HTMLInputElement).value).toBe('longenough1');
  });

  it('renders zod validation errors in the active i18n language (Spanish)', async () => {
    // Regression for 2026-05-18: schemas previously hard-coded English strings
    // (`z.string().email('Enter a valid email address.')`). On a page in
    // Spanish, the zod error rendered in English. Schemas now emit i18n keys
    // (`'auth.validation.email'`) and the form component resolves them via
    // t(...) at render time.
    const { act } = await import('@testing-library/react');
    await act(async () => {
      await i18n.changeLanguage('es');
    });
    try {
      const user = userEvent.setup();
      renderAt('/password-reset');
      // Submit with an invalid email → schema emits the key
      // 'auth.validation.email', which en.json renders as "Enter a valid..."
      // and es.json renders as "Introduce un correo electrónico válido.".
      await user.type(screen.getByLabelText(/correo electrónico/i), 'not-an-email');
      await user.click(screen.getByRole('button', { name: /enviar enlace/i }));
      await waitFor(() =>
        expect(screen.getByText(/Introduce un correo electrónico válido/i)).toBeDefined(),
      );
      expect(screen.queryByText(/Enter a valid email address/i)).toBeNull();
    } finally {
      await act(async () => {
        await i18n.changeLanguage('en');
      });
    }
  });

  it('confirm 422 VALIDATION_ERROR maps details to field error', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/password-reset/confirm') {
        return new Response(
          JSON.stringify({
            error: {
              code: 'VALIDATION_ERROR',
              message: 'policy',
              details: [{ field: 'newPassword', message: 'Password has been seen in breaches.' }],
            },
          }),
          { status: 422, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderAt('/password-reset#token=abc');
    await user.type(screen.getByLabelText(/^new password$/i), 'longenough1');
    await user.type(screen.getByLabelText(/confirm new password/i), 'longenough1');
    await user.click(screen.getByRole('button', { name: /reset password/i }));
    await waitFor(() => expect(screen.getByText(/seen in breaches/i)).toBeDefined());
  });
});
