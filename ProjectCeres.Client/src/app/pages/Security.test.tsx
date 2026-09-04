import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '../i18n/i18n';
import { AuthProvider } from '../auth/auth-context';
import { Security } from './Security';
import { clearXsrfTokenCacheForTests } from '../auth/csrf';

// Stage 12.9: these surfaces now route reauth through the step-up dialog, so they
// depend on StepUpProvider's context. Mocked as a pass-through here — the dialog's
// own behaviour (open, collect password, replay, cancel) has a dedicated suite in
// use-step-up.test.tsx. Same pattern as SessionsPage.test.tsx.
const requireStepUp = vi.fn(<T,>(action: () => Promise<T>) => action());
vi.mock('../auth/use-step-up', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../auth/use-step-up')>()),
  useStepUp: () => ({ requireStepUp }),
}));


function meResponse(twoFactorEnabled: boolean) {
  return new Response(
    JSON.stringify({
      userId: '00000000-0000-0000-0000-000000000001',
      email: 'a@b.test',
      twoFactorEnabled,
      lastReauthAt: Math.floor(Date.now() / 1000),
      backupCodesRemaining: twoFactorEnabled ? 10 : 0,
      usedBackupCodeAtLastLogin: false,
    }),
    { status: 200, headers: { 'Content-Type': 'application/json' } },
  );
}

function renderSecurity(initial: { twoFactorEnabled: boolean }) {
  const fetchSpy = vi.spyOn(global, 'fetch');
  clearXsrfTokenCacheForTests();
  fetchSpy.mockImplementation(async (url) => {
    if (typeof url === 'string' && url === '/api/auth/me') {
      return meResponse(initial.twoFactorEnabled);
    }
    if (typeof url === 'string' && url === '/api/auth/csrf') {
      return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
    }
    return new Response(null, { status: 204 });
  });
  document.cookie = '__Host-XSRF=test; path=/';
  return {
    fetchSpy,
    rendered: render(
      <I18nextProvider i18n={i18n}>
        <AuthProvider>
          <MemoryRouter initialEntries={['/security']}>
            <Routes>
              <Route path="/security" element={<Security />} />
            </Routes>
          </MemoryRouter>
        </AuthProvider>
      </I18nextProvider>,
    ),
  };
}

describe('Security page', () => {
  beforeEach(() => {
    // jsdom doesn't ship clipboard; tests that use it stub per-test.
  });
  afterEach(() => vi.restoreAllMocks());

  it('renders disabled state with Enable button when twoFactorEnabled is false', async () => {
    renderSecurity({ twoFactorEnabled: false });
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /set up two-factor sign-in/i })).toBeDefined(),
    );
  });

  it('renders enabled state with Regenerate + Disable buttons when twoFactorEnabled is true', async () => {
    renderSecurity({ twoFactorEnabled: true });
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /regenerate backup codes/i })).toBeDefined(),
    );
    expect(screen.getByRole('button', { name: /turn off two-factor sign-in/i })).toBeDefined();
  });

  it('routes the enroll call through requireStepUp', async () => {
    // The 12.9 rewiring had no assertion on any of its four call sites except the
    // sessions list: structurally the call moved inside requireStepUp, but nothing
    // pinned it, so reverting to a bare apiFetch stayed green. Found by the 12.8
    // writer review.
    const { fetchSpy } = renderSecurity({ twoFactorEnabled: false });
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') return meResponse(false);
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      return new Response(null, { status: 204 });
    });

    await userEvent.click(await screen.findByRole('button', { name: /set up two-factor/i }));

    await waitFor(() => expect(requireStepUp).toHaveBeenCalled());
  });

  it('clicking Enable fires POST /api/auth/mfa/enroll and advances to wizard step 1', async () => {
    const { fetchSpy } = renderSecurity({ twoFactorEnabled: false });
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') return meResponse(false);
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/mfa/enroll') {
        return new Response(
          JSON.stringify({
            otpAuthUri: 'otpauth://totp/Ceres:a@b.test?secret=JBSWY3DPEHPK3PXP&issuer=Ceres',
            manualEntryKey: 'JBSW Y3DP EHPK 3PXP',
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });

    const user = userEvent.setup();
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /set up two-factor sign-in/i })).toBeDefined(),
    );
    await user.click(screen.getByRole('button', { name: /set up two-factor sign-in/i }));

    await waitFor(() => expect(screen.getByText(/scan with your authenticator app/i)).toBeDefined());
    // Manual-entry disclosure is collapsed by default but the key is in the DOM
    expect(screen.getByText('JBSW Y3DP EHPK 3PXP')).toBeDefined();
  });

  it('on 409 MFA_ALREADY_ENROLLED, refreshes auth and stays on Security (no wizard)', async () => {
    // AuthProvider's mount-time /me call lands BEFORE this test's inner
    // mockImplementation replaces the outer renderSecurity stub. Counter
    // ticks only on calls under the inner mock — first call here is the
    // /me refresh AFTER the 409. That single call returns enabled=true.
    const { fetchSpy } = renderSecurity({ twoFactorEnabled: false });
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return meResponse(true);
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/mfa/enroll') {
        return new Response(
          JSON.stringify({ error: { code: 'MFA_ALREADY_ENROLLED', message: 'no' } }),
          { status: 409, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });

    const user = userEvent.setup();
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /set up two-factor sign-in/i })).toBeDefined(),
    );
    await user.click(screen.getByRole('button', { name: /set up two-factor sign-in/i }));

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /regenerate backup codes/i })).toBeDefined(),
    );
  });

  it('clicking Disable opens AlertDialog; confirm fires POST /api/auth/mfa/disable and re-renders disabled state', async () => {
    // Same gotcha as the 409 test: AuthProvider's mount-time /me call uses
    // the OUTER mock (enabled=true). Inner mock only sees the refresh()
    // call AFTER disable succeeds → always return enabled=false.
    const { fetchSpy } = renderSecurity({ twoFactorEnabled: true });
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return meResponse(false);
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/mfa/disable') {
        return new Response(null, { status: 204 });
      }
      return new Response(null, { status: 204 });
    });

    const user = userEvent.setup();
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /turn off two-factor sign-in/i })).toBeDefined(),
    );
    await user.click(screen.getByRole('button', { name: /turn off two-factor sign-in/i }));

    // AlertDialog has its own confirm button labeled "Turn off"
    const confirm = await screen.findByRole('button', { name: /^turn off$/i });
    await user.click(confirm);

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /set up two-factor sign-in/i })).toBeDefined(),
    );
  });
});
