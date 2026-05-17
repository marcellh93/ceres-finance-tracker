import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import i18n from '@/app/i18n/i18n';
import { AuthProvider } from '@/app/auth/auth-context';
import { AvatarMenu } from './AvatarMenu';

const AUTHED_ME_RESPONSE = new Response(
  JSON.stringify({
    userId: '00000000-0000-0000-0000-000000000001',
    email: 'a@b.test',
    twoFactorEnabled: false,
    lastReauthAt: null,
    backupCodesRemaining: 0,
    usedBackupCodeAtLastLogin: false,
  }),
  { status: 200, headers: { 'Content-Type': 'application/json' } },
);

let fetchSpy: ReturnType<typeof vi.spyOn>;

beforeEach(async () => {
  await i18n.changeLanguage('en');
  fetchSpy = vi.spyOn(global, 'fetch');
  // First call is /api/auth/me from AuthProvider's initial refresh.
  // Test cases override with mockImplementationOnce for the POST.
  fetchSpy.mockImplementation(async (input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    if (url.includes('/api/auth/me')) return AUTHED_ME_RESPONSE.clone();
    return new Response(null, { status: 204 });
  });
});

afterEach(() => vi.restoreAllMocks());

function renderMenu(landingPath = '/') {
  return render(
    <I18nextProvider i18n={i18n}>
      <AuthProvider>
        <MemoryRouter initialEntries={[landingPath]}>
          <Routes>
            <Route path="/" element={<AvatarMenu />} />
            <Route path="/login" element={<div>login-page</div>} />
          </Routes>
        </MemoryRouter>
      </AuthProvider>
    </I18nextProvider>,
  );
}

describe('AvatarMenu', () => {
  it('shows nothing in the menu before the trigger is clicked', () => {
    renderMenu();
    expect(screen.queryByRole('menuitem', { name: 'Profile' })).toBeNull();
  });

  it('renders Profile, Security, and Logout in that order when opened', async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole('button', { name: /open user menu/i }));
    const items = await screen.findAllByRole('menuitem');
    expect(items.map((i) => i.textContent?.trim())).toEqual([
      'Profile',
      'Security',
      'Logout',
    ]);
  });

  it('clicking Logout opens the confirmation dialog with destructive Sign-out button', async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole('button', { name: /open user menu/i }));
    await user.click(await screen.findByRole('menuitem', { name: 'Logout' }));
    expect(await screen.findByText(/are you sure you want to sign out/i)).toBeDefined();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeDefined();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDefined();
  });

  it('confirming sign-out POSTs to /api/auth/logout and navigates to /login', async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole('button', { name: /open user menu/i }));
    await user.click(await screen.findByRole('menuitem', { name: 'Logout' }));
    await user.click(await screen.findByRole('button', { name: 'Sign out' }));

    await waitFor(() => {
      const logoutCall = fetchSpy.mock.calls.find(([url]) => {
        const u = typeof url === 'string' ? url : (url as URL | Request).toString();
        return u.includes('/api/auth/logout');
      });
      expect(logoutCall).toBeDefined();
      expect(logoutCall?.[1]?.method).toBe('POST');
    });
    await waitFor(() => expect(screen.getByText('login-page')).toBeDefined());
  });

  it('cancel closes the dialog without calling logout', async () => {
    const user = userEvent.setup();
    renderMenu();
    await user.click(screen.getByRole('button', { name: /open user menu/i }));
    await user.click(await screen.findByRole('menuitem', { name: 'Logout' }));
    await user.click(await screen.findByRole('button', { name: 'Cancel' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    const logoutCall = fetchSpy.mock.calls.find(([url]) => {
      const u = typeof url === 'string' ? url : (url as URL | Request).toString();
      return u.includes('/api/auth/logout');
    });
    expect(logoutCall).toBeUndefined();
  });
});
