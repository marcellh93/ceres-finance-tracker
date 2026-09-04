import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { I18nextProvider } from 'react-i18next';
import { MemoryRouter } from 'react-router-dom';
import i18n from '../../i18n/i18n';
import { AuthProvider } from '../../auth/auth-context';
import { RegenerateBackupCodesDialog } from './RegenerateBackupCodesDialog';
import { clearXsrfTokenCacheForTests } from '../../auth/csrf';
import { ReauthCancelledError } from '../../auth/use-step-up';

// sonner renders into a <Toaster> that this component tree does not mount, so assert
// the call rather than the rendered toast — the behaviour under test is "the failure
// is surfaced at all", not sonner's rendering.
const toastError = vi.fn();
vi.mock('sonner', () => ({ toast: { error: (...a: unknown[]) => toastError(...a), success: vi.fn() } }));

// Stage 12.9: these surfaces now route reauth through the step-up dialog, so they
// depend on StepUpProvider's context. Mocked as a pass-through here — the dialog's
// own behaviour (open, collect password, replay, cancel) has a dedicated suite in
// use-step-up.test.tsx. Same pattern as SessionsPage.test.tsx.
const requireStepUp = vi.fn(<T,>(action: () => Promise<T>) => action());
vi.mock('../../auth/use-step-up', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../auth/use-step-up')>()),
  useStepUp: () => ({ requireStepUp }),
}));


function mount() {
  return render(
    <I18nextProvider i18n={i18n}>
      <AuthProvider>
        <MemoryRouter>
          <RegenerateBackupCodesDialog />
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

  it('surfaces a failure instead of closing silently', async () => {
    // Regression for the 12.8 review block. Removing the old reauth dead-end left
    // this path rendering NOTHING on failure, behind a comment claiming a toast
    // that did not exist in the file. On the one screen where guessing wrong means
    // losing account access, a silent close is the worst possible outcome.
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        return new Response(
          JSON.stringify({
            userId: '00000000-0000-0000-0000-000000000001', email: 'a@b.test',
            twoFactorEnabled: true, lastReauthAt: Math.floor(Date.now() / 1000),
            backupCodesRemaining: 10, usedBackupCodeAtLastLogin: false,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url.includes('backup-codes/regenerate')) {
        return new Response(JSON.stringify({ error: { code: 'SERVER_ERROR', message: 'boom' } }),
          { status: 500, headers: { 'Content-Type': 'application/json' } });
      }
      return new Response(null, { status: 204 });
    });

    const user = userEvent.setup();
    mount();
    await user.click(screen.getByRole('button', { name: /regenerate backup codes/i }));
    await user.click(await screen.findByRole('button', { name: /^regenerate codes$/i }));

    await waitFor(() => expect(toastError).toHaveBeenCalled());
    expect(String(toastError.mock.calls[0][0])).toMatch(/couldn't regenerate your backup codes/i);
  });

  it('routes the regenerate call through requireStepUp', async () => {
    // The rewiring itself was untested: structurally the call moved, but nothing
    // asserted it. Without this, reverting to a bare apiFetch stays green.
    const user = userEvent.setup();
    mount();
    await user.click(screen.getByRole('button', { name: /regenerate backup codes/i }));
    await user.click(await screen.findByRole('button', { name: /^regenerate codes$/i }));

    await waitFor(() => expect(requireStepUp).toHaveBeenCalled());
  });

  it('on a cancelled reauth prompt, the dialog closes (no stacked surfaces)', async () => {
    // Stage 12.9 contract change: REAUTH_REQUIRED no longer bubbles to the parent as
    // a dead-end message — requireStepUp opens the password dialog and replays the
    // call. What must NOT regress is the 2026-05-18 bug this test was written for:
    // the dialog stayed stuck open in 'pending' while another surface rendered
    // behind it. If the user CANCELS the password prompt, this dialog must still
    // close rather than sit pending forever. requireStepUp is mocked to reject with
    // ReauthCancelledError to drive exactly that path.
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

    requireStepUp.mockRejectedValueOnce(new ReauthCancelledError());

    const user = userEvent.setup();
    mount();

    // Open the dialog via the regenerate button (the trigger).
    await user.click(screen.getByRole('button', { name: /regenerate backup codes/i }));
    // Dialog open → has the destructive action button labelled "Regenerate codes".
    const confirm = await screen.findByRole('button', { name: /^regenerate codes$/i });
    await user.click(confirm);

    // Dialog DOM gone (no more "Regenerate codes" action button in the body) — the
    // stuck-in-pending regression this test exists for.
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: /^regenerate codes$/i })).toBeNull(),
    );
  });

  it('on success, refetches /api/auth/me so consumers see the fresh backupCodesRemaining', async () => {
    // Regression: regeneration succeeded but the dashboard
    // BackupCodeLoginBanner kept rendering because the auth context's
    // cached MeResponse still held the old low count. Fix calls
    // auth.refresh() before transitioning to the success state, which
    // re-fires GET /api/auth/me through apiFetch.
    let regenerateCount = 0;
    let meCallCountAfterRegenerate = 0;
    let regenerateResolved = false;
    fetchSpy.mockImplementation(async (url) => {
      if (typeof url === 'string' && url === '/api/auth/me') {
        if (regenerateResolved) meCallCountAfterRegenerate += 1;
        return new Response(
          JSON.stringify({
            userId: '00000000-0000-0000-0000-000000000001',
            email: 'a@b.test',
            twoFactorEnabled: true,
            lastReauthAt: Math.floor(Date.now() / 1000),
            backupCodesRemaining: regenerateResolved ? 10 : 3,
            usedBackupCodeAtLastLogin: false,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        );
      }
      if (typeof url === 'string' && url === '/api/auth/csrf') {
        return new Response(null, { status: 204, headers: { 'X-XSRF-TOKEN': 'tok' } });
      }
      if (typeof url === 'string' && url === '/api/auth/mfa/backup-codes/regenerate') {
        regenerateCount += 1;
        regenerateResolved = true;
        return new Response(
          JSON.stringify({
            backupCodes: [
              'AAAA-BBBB-CCCC-DD11', 'AAAA-BBBB-CCCC-DD22', 'AAAA-BBBB-CCCC-DD33',
              'AAAA-BBBB-CCCC-DD44', 'AAAA-BBBB-CCCC-DD55', 'AAAA-BBBB-CCCC-DD66',
              'AAAA-BBBB-CCCC-DD77', 'AAAA-BBBB-CCCC-DD88', 'AAAA-BBBB-CCCC-DD99',
              'AAAA-BBBB-CCCC-DDAA',
            ],
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        );
      }
      return new Response(null, { status: 204 });
    });

    const user = userEvent.setup();
    mount();
    await user.click(screen.getByRole('button', { name: /regenerate backup codes/i }));
    await user.click(await screen.findByRole('button', { name: /^regenerate codes$/i }));

    // The dialog should transition to the showCodes state (one of the new
    // codes appears in the DOM).
    await waitFor(() => {
      expect(screen.getByText('AAAA-BBBB-CCCC-DD11')).toBeInTheDocument();
    });

    expect(regenerateCount).toBe(1);
    expect(meCallCountAfterRegenerate).toBeGreaterThanOrEqual(1);
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
