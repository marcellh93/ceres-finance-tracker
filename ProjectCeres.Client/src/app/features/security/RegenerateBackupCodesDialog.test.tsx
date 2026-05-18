import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { I18nextProvider } from 'react-i18next';
import { MemoryRouter } from 'react-router-dom';
import i18n from '../../i18n/i18n';
import { AuthProvider } from '../../auth/auth-context';
import { RegenerateBackupCodesDialog } from './RegenerateBackupCodesDialog';
import { clearXsrfTokenCacheForTests } from '../../auth/csrf';

function mount(onReauthRequired = vi.fn()) {
  return render(
    <I18nextProvider i18n={i18n}>
      <AuthProvider>
        <MemoryRouter>
          <RegenerateBackupCodesDialog onReauthRequired={onReauthRequired} />
        </MemoryRouter>
      </AuthProvider>
    </I18nextProvider>,
  );
}

describe('RegenerateBackupCodesDialog', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    fetchSpy = vi.spyOn(global, 'fetch');
    clearXsrfTokenCacheForTests();
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({
            userId: '00000000-0000-0000-0000-000000000001',
            email: 'a@b.test',
            twoFactorEnabled: true,
            lastReauthAt: Math.floor(Date.now() / 1000),
            backupCodesRemaining: 10,
            usedBackupCodeAtLastLogin: false,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      return new Response(null, { status: 204 });
    });
    document.cookie = '__Host-XSRF=test; path=/';
  });

  afterEach(() => vi.restoreAllMocks());

  it('on REAUTH_REQUIRED, dialog closes AND parent is notified (no stacked surfaces)', async () => {
    // Regression for the 2026-05-18 bug where REAUTH_REQUIRED kept the
    // dialog stuck open (state stayed 'pending') and the parent's
    // reauth alert rendered behind the still-open dialog.
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({
            userId: '00000000-0000-0000-0000-000000000001',
            email: 'a@b.test',
            twoFactorEnabled: true,
            lastReauthAt: Math.floor(Date.now() / 1000),
            backupCodesRemaining: 10,
            usedBackupCodeAtLastLogin: false,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/mfa/backup-codes/regenerate') {
        return new Response(
          JSON.stringify({ error: { code: 'REAUTH_REQUIRED', message: 'reauth' } }),
          { status: 401, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });

    const onReauthRequired = vi.fn();
    const user = userEvent.setup();
    mount(onReauthRequired);

    // Open the dialog via the regenerate button (the trigger).
    await user.click(screen.getByRole('button', { name: /regenerate backup codes/i }));
    // Dialog open → has the destructive action button labelled "Regenerate codes".
    const confirm = await screen.findByRole('button', { name: /^regenerate codes$/i });
    await user.click(confirm);

    // Parent notified.
    await waitFor(() => expect(onReauthRequired).toHaveBeenCalledTimes(1));
    // Dialog DOM gone (no more "Regenerate codes" action button in the body).
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: /^regenerate codes$/i })).toBeNull(),
    );
  });

  it('Cancel button works even AFTER Confirm has been clicked (pending state cancellable)', async () => {
    // Regression: Cancel was a no-op when state==='pending' because the
    // onOpenChange handler only reset state from 'confirming'. Users who
    // clicked Regenerate and then changed their mind (or hit a network
    // hiccup) had no way to dismiss the dialog.
    let resolveFetch: ((r: Response) => void) | null = null;
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({
            userId: '00000000-0000-0000-0000-000000000001',
            email: 'a@b.test',
            twoFactorEnabled: true,
            lastReauthAt: Math.floor(Date.now() / 1000),
            backupCodesRemaining: 10,
            usedBackupCodeAtLastLogin: false,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/mfa/backup-codes/regenerate') {
        // Hang the response so the dialog stays in 'pending' while we click Cancel.
        return new Promise<Response>((r) => {
          resolveFetch = r;
        });
      }
      return new Response(null, { status: 204 });
    });

    const user = userEvent.setup();
    mount();
    await user.click(screen.getByRole('button', { name: /regenerate backup codes/i }));
    await user.click(await screen.findByRole('button', { name: /^regenerate codes$/i }));

    // Now the dialog is in 'pending' (the regenerate fetch is hanging).
    // Click Cancel.
    const cancel = await screen.findByRole('button', { name: /^cancel$/i });
    await user.click(cancel);

    // Dialog gone — the destructive action button is no longer in the DOM.
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: /^regenerate codes$/i })).toBeNull(),
    );

    // Resolve the dangling fetch so React doesn't warn.
    if (resolveFetch) (resolveFetch as (r: Response) => void)(new Response(null, { status: 204 }));
  });
});
