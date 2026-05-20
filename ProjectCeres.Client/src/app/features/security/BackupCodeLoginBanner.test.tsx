import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { I18nextProvider } from 'react-i18next';
import { MemoryRouter } from 'react-router-dom';
import i18n from '../../i18n/i18n';
import { AuthProvider } from '../../auth/auth-context';
import { BackupCodeLoginBanner } from './BackupCodeLoginBanner';
import { clearXsrfTokenCacheForTests } from '../../auth/csrf';

type MeShape = {
  userId: string;
  email: string;
  twoFactorEnabled: boolean;
  lastReauthAt: number | null;
  backupCodesRemaining: number;
  usedBackupCodeAtLastLogin: boolean;
};

const baseMe: MeShape = {
  userId: '00000000-0000-0000-0000-000000000001',
  email: 'a@b.test',
  twoFactorEnabled: true,
  lastReauthAt: Math.floor(Date.now() / 1000),
  backupCodesRemaining: 5,
  usedBackupCodeAtLastLogin: false,
};

function mockMe(me: MeShape | null) {
  const spy = vi.spyOn(global, 'fetch');
  spy.mockImplementation(async (url) => {
    if (typeof url === 'string' && url === '/api/auth/me') {
      if (me === null) {
        return new Response(null, { status: 401 });
      }
      return new Response(JSON.stringify(me), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    }
    return new Response(null, { status: 204 });
  });
  return spy;
}

function mount() {
  return render(
    <I18nextProvider i18n={i18n}>
      <AuthProvider>
        <MemoryRouter>
          <BackupCodeLoginBanner />
        </MemoryRouter>
      </AuthProvider>
    </I18nextProvider>,
  );
}

describe('BackupCodeLoginBanner', () => {
  beforeEach(() => {
    clearXsrfTokenCacheForTests();
    document.cookie = '__Host-XSRF=test; path=/';
  });

  afterEach(() => vi.restoreAllMocks());

  it('renders the re-enrol CTA when usedBackupCodeAtLastLogin is true', async () => {
    mockMe({ ...baseMe, usedBackupCodeAtLastLogin: true, backupCodesRemaining: 5 });
    mount();

    await waitFor(() => {
      expect(screen.getByRole('status')).toBeInTheDocument();
    });
    expect(screen.getByRole('link', { name: /re-enrol authenticator/i })).toBeInTheDocument();
    expect(screen.getByText(/5 codes left/i)).toBeInTheDocument();
  });

  it('renders the regenerate CTA when usedBackupCodeAtLastLogin is false and codes are low', async () => {
    mockMe({ ...baseMe, usedBackupCodeAtLastLogin: false, backupCodesRemaining: 3 });
    mount();

    await waitFor(() => {
      expect(screen.getByRole('status')).toBeInTheDocument();
    });
    expect(screen.getByRole('link', { name: /regenerate backup codes/i })).toBeInTheDocument();
    expect(screen.getByText(/3 left/i)).toBeInTheDocument();
  });

  it('renders nothing when backupCodesRemaining is above the threshold', async () => {
    mockMe({ ...baseMe, backupCodesRemaining: 8 });
    const { container } = mount();

    // Wait for AuthProvider to finish its /me round-trip before asserting absence.
    await waitFor(() => {
      expect(vi.mocked(global.fetch)).toHaveBeenCalledWith('/api/auth/me', expect.anything());
    });
    expect(container.querySelector('[role="status"]')).toBeNull();
  });

  it('renders nothing when MFA is disabled', async () => {
    mockMe({ ...baseMe, twoFactorEnabled: false, backupCodesRemaining: 0 });
    const { container } = mount();

    await waitFor(() => {
      expect(vi.mocked(global.fetch)).toHaveBeenCalledWith('/api/auth/me', expect.anything());
    });
    expect(container.querySelector('[role="status"]')).toBeNull();
  });

  it('renders nothing when the user is anonymous', async () => {
    mockMe(null);
    const { container } = mount();

    await waitFor(() => {
      expect(vi.mocked(global.fetch)).toHaveBeenCalledWith('/api/auth/me', expect.anything());
    });
    expect(container.querySelector('[role="status"]')).toBeNull();
  });

  it('dismiss hides the banner for the current render but a fresh mount restores it', async () => {
    mockMe({ ...baseMe, usedBackupCodeAtLastLogin: true, backupCodesRemaining: 4 });
    const first = mount();

    await waitFor(() => {
      expect(screen.getByRole('status')).toBeInTheDocument();
    });
    await userEvent.click(screen.getByRole('button', { name: /dismiss/i }));
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
    first.unmount();

    mount();
    await waitFor(() => {
      expect(screen.getByRole('status')).toBeInTheDocument();
    });
  });

  it('uses the singular body when only one code remains', async () => {
    mockMe({ ...baseMe, usedBackupCodeAtLastLogin: true, backupCodesRemaining: 1 });
    mount();

    await waitFor(() => {
      expect(screen.getByText(/1 code left/i)).toBeInTheDocument();
    });
    expect(screen.queryByText(/1 codes left/i)).not.toBeInTheDocument();
  });
});
