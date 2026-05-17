import { render, screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { AuthProvider } from '../../auth/auth-context';
import { LoginTotp } from './LoginTotp';
import { clearXsrfTokenCacheForTests } from '../../auth/csrf';

function renderLoginTotp() {
  return render(
    <I18nextProvider i18n={i18n}>
      <AuthProvider>
        <MemoryRouter initialEntries={['/login/totp']}>
          <Routes>
            <Route path="/login/totp" element={<LoginTotp />} />
            <Route path="/" element={<div>dashboard</div>} />
            <Route path="/login" element={<div>login</div>} />
            <Route path="/account/unlock" element={<div>unlock page</div>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </I18nextProvider>,
  );
}

describe('LoginTotp page', () => {
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
          headers: { 'X-XSRF-TOKEN': 'test-csrf-token' },
        });
      }
      return new Response(null, { status: 204 });
    });
    document.cookie = '__Host-XSRF=test; path=/';
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders OTP cells, submit, backup-code link, and back-to-sign-in link', () => {
    renderLoginTotp();
    expect(screen.getByLabelText(/verification code/i)).toBeDefined();
    expect(screen.getByRole('button', { name: /^verify$/i })).toBeDefined();
    expect(screen.getByRole('button', { name: /lost your device/i })).toBeDefined();
    expect(screen.getByRole('link', { name: /back to sign in/i })).toBeDefined();
  });

  it('auto-submits when 6 digits typed; on 204 navigates to /', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/login/totp') {
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLoginTotp();
    await user.type(screen.getByLabelText(/verification code/i), '123456');
    await waitFor(() =>
      expect(fetchSpy).toHaveBeenCalledWith(
        '/api/auth/login/totp',
        expect.objectContaining({ method: 'POST' }),
      ),
    );
    await waitFor(() => expect(screen.getByText('dashboard')).toBeDefined());
  });

  it('on 401 INVALID_MFA_CODE renders the inline error', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/login/totp') {
        return new Response(
          JSON.stringify({ error: { code: 'INVALID_MFA_CODE', message: 'no' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLoginTotp();
    await user.type(screen.getByLabelText(/verification code/i), '111111');
    await waitFor(() => expect(screen.getByRole('alert').textContent).toMatch(/invalid code/i));
  });

  it('on 401 ACCOUNT_LOCKED_OUT navigates to /account/unlock', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/login/totp') {
        return new Response(
          JSON.stringify({ error: { code: 'ACCOUNT_LOCKED_OUT', message: 'no' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLoginTotp();
    await user.type(screen.getByLabelText(/verification code/i), '222222');
    await waitFor(() => expect(screen.getByText('unlock page')).toBeDefined());
  });

  it('on 401 UNAUTHENTICATED navigates to /login (expired)', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/login/totp') {
        return new Response(
          JSON.stringify({ error: { code: 'UNAUTHENTICATED', message: 'no' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLoginTotp();
    await user.type(screen.getByLabelText(/verification code/i), '333333');
    await waitFor(() => expect(screen.getByText('login')).toBeDefined());
  });

  it('on 429 with Retry-After surfaces the countdown message', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/login/totp') {
        return new Response(null, { status: 429, headers: { 'Retry-After': '30' } });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLoginTotp();
    await user.type(screen.getByLabelText(/verification code/i), '444444');
    await waitFor(() =>
      expect(screen.getByRole('alert').textContent).toMatch(/too many attempts.*30 seconds/i),
    );
  });

  it('on 429 with no Retry-After surfaces the no-countdown variant', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/login/totp') {
        return new Response(null, { status: 429 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLoginTotp();
    await user.type(screen.getByLabelText(/verification code/i), '555555');
    await waitFor(() =>
      expect(screen.getByRole('alert').textContent).toMatch(/too many attempts.*try again in a moment/i),
    );
  });

  it('backup-code mode: link toggles form; submit posts the typed string', async () => {
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
      if (typeof url === 'string' && url === '/api/auth/login/totp') {
        const body = JSON.parse((init?.body as string) ?? '{}');
        if (body.code === 'BACKUP-XYZ-123') return new Response(null, { status: 204 });
        return new Response(null, { status: 401 });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLoginTotp();
    await user.click(screen.getByRole('button', { name: /lost your device/i }));
    await user.type(screen.getByLabelText(/backup code/i), 'BACKUP-XYZ-123');
    await user.click(screen.getByRole('button', { name: /^verify$/i }));
    await waitFor(() => expect(screen.getByText('dashboard')).toBeDefined());
  });

  it('backup-code mode: "use verification code instead" toggles back and clears state', async () => {
    const user = userEvent.setup();
    renderLoginTotp();
    await user.click(screen.getByRole('button', { name: /lost your device/i }));
    await user.type(screen.getByLabelText(/backup code/i), 'OLDVAL');
    await user.click(screen.getByRole('button', { name: /use verification code instead/i }));
    expect(screen.getByLabelText(/verification code/i)).toBeDefined();
    expect(screen.queryByLabelText(/backup code/i)).toBeNull();
  });

  it('Verify button label shows "Verifying" while in flight', async () => {
    let resolveResponse: (r: Response) => void = () => {};
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
      if (typeof url === 'string' && url === '/api/auth/login/totp') {
        return new Promise<Response>((resolve) => {
          resolveResponse = resolve;
        });
      }
      return new Response(null, { status: 204 });
    });
    const user = userEvent.setup();
    renderLoginTotp();
    await user.type(screen.getByLabelText(/verification code/i), '666666');
    await waitFor(() => expect(screen.getByRole('button', { name: /verifying/i })).toBeDefined());
    act(() => resolveResponse(new Response(null, { status: 204 })));
  });
});
