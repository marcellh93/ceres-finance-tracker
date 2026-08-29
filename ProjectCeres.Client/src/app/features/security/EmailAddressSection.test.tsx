import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import i18n from '../../i18n/i18n';
import { EmailAddressSection } from './EmailAddressSection';
import { ReauthCancelledError } from '../../auth/use-step-up';

const requireStepUp = vi.fn(<T,>(action: () => Promise<T>) => action());
vi.mock('../../auth/use-step-up', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../auth/use-step-up')>()),
  useStepUp: () => ({ requireStepUp }),
}));

const apiFetch = vi.fn();
vi.mock('../../lib/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../lib/api-client')>()),
  apiFetch: (...args: unknown[]) => apiFetch(...args),
}));

vi.mock('../../auth/auth-context', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../auth/auth-context')>()),
  useAuth: () => ({ user: { email: 'current@example.com' } }),
}));

function mount() {
  return render(
    <I18nextProvider i18n={i18n}>
      <MemoryRouter>
        <EmailAddressSection />
      </MemoryRouter>
    </I18nextProvider>,
  );
}

const noPending = { ok: true, data: { pending: false, maskedEmail: null, expiresAt: null, expired: false } };

describe('EmailAddressSection', () => {
  beforeEach(() => {
    apiFetch.mockReset();
    requireStepUp.mockReset();
    requireStepUp.mockImplementation(<T,>(action: () => Promise<T>) => action());
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('shows the current address', async () => {
    apiFetch.mockResolvedValue(noPending);
    mount();
    expect(await screen.findByText('current@example.com')).toBeInTheDocument();
  });

  it('shows the masked address in the pending banner, never a full one', async () => {
    // The banner is the surface a passer-by can read over the owner's shoulder.
    // The server masks it; this pins that the page renders what the server sent
    // rather than helpfully reconstructing anything.
    apiFetch.mockResolvedValue({
      ok: true,
      data: {
        pending: true,
        maskedEmail: 'n•••••@e•••••',
        expiresAt: new Date(Date.now() + 20 * 60_000).toISOString(),
        expired: false,
      },
    });

    mount();

    expect(await screen.findByText(/n•••••@e•••••/)).toBeInTheDocument();
    // The banner must not carry the pending target in full. Scoped to the banner:
    // the CURRENT address is shown in full above it, and correctly so — it is the
    // user's own address, already known to anyone reading the page.
    const banner = screen.getByRole('status');
    expect(banner).not.toHaveTextContent(/new@example\.com/);
    expect(banner).not.toHaveTextContent(/@example\.com/);
  });

  it('counts down the remaining minutes', async () => {
    apiFetch.mockResolvedValue({
      ok: true,
      data: {
        pending: true,
        maskedEmail: 'n•••••@e•••••',
        expiresAt: new Date(Date.now() + 24 * 60_000).toISOString(),
        expired: false,
      },
    });

    mount();

    expect(await screen.findByText(/expires in 24 minutes/i)).toBeInTheDocument();
  });

  it('says "less than a minute" rather than "0 minutes" at the boundary', async () => {
    // "Expires in 0 minutes" reads as already-dead and is the kind of copy that
    // makes a user give up on a link that still works.
    apiFetch.mockResolvedValue({
      ok: true,
      data: {
        pending: true,
        maskedEmail: 'n•••••@e•••••',
        expiresAt: new Date(Date.now() + 5_000).toISOString(),
        expired: false,
      },
    });

    mount();

    expect(await screen.findByText(/less than a minute/i)).toBeInTheDocument();
  });

  it('shows the expired state with a resend action, not a silent empty banner', async () => {
    // The user who missed the 30-minute window is precisely who needs telling.
    apiFetch.mockResolvedValue({
      ok: true,
      data: {
        pending: true,
        maskedEmail: 'n•••••@e•••••',
        expiresAt: new Date(Date.now() - 60_000).toISOString(),
        expired: true,
      },
    });

    mount();

    expect(await screen.findByText(/that link expired/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /send a new link/i })).toBeInTheDocument();
    // No countdown on a dead link.
    expect(screen.queryByText(/expires in/i)).toBeNull();
  });

  it('submits through requireStepUp so the reauth prompt can open', async () => {
    // The server gates this endpoint on recent auth. Calling apiFetch directly
    // would give the user a dead end instead of the password dialog.
    apiFetch.mockResolvedValueOnce(noPending).mockResolvedValue({ ok: true });
    const user = userEvent.setup();
    mount();

    await user.click(await screen.findByRole('button', { name: /change email address/i }));
    await user.type(screen.getByLabelText(/new email address/i), 'new@example.com');
    await user.click(screen.getByRole('button', { name: /send confirmation link/i }));

    await waitFor(() => expect(requireStepUp).toHaveBeenCalled());
  });

  it('shows the full new address in the just-submitted message', async () => {
    // Masking is for the returning-visitor banner. Here the user typed it seconds
    // ago and confirming what they typed is the whole point of the message.
    apiFetch.mockResolvedValueOnce(noPending).mockResolvedValue({ ok: true });
    const user = userEvent.setup();
    mount();

    await user.click(await screen.findByRole('button', { name: /change email address/i }));
    await user.type(screen.getByLabelText(/new email address/i), 'new@example.com');
    await user.click(screen.getByRole('button', { name: /send confirmation link/i }));

    expect(await screen.findByText(/new@example\.com/)).toBeInTheDocument();
  });

  it('explains that the old address was emailed too', async () => {
    // "Check both inboxes" assumes a mental model the user does not have. The
    // message must say WHY the other address was contacted.
    apiFetch.mockResolvedValueOnce(noPending).mockResolvedValue({ ok: true });
    const user = userEvent.setup();
    mount();

    await user.click(await screen.findByRole('button', { name: /change email address/i }));
    await user.type(screen.getByLabelText(/new email address/i), 'new@example.com');
    await user.click(screen.getByRole('button', { name: /send confirmation link/i }));

    expect(await screen.findByText(/in case this wasn't you/i)).toBeInTheDocument();
  });

  it.each([
    ['EMAIL_UNCHANGED', /already your address/i],
    ['EMAIL_ALREADY_IN_USE', /already registered to another account/i],
    ['RATE_LIMITED', /too many attempts/i],
  ])('surfaces the %s refusal in its own words', async (code, expected) => {
    apiFetch.mockResolvedValueOnce(noPending).mockResolvedValue({ ok: false, code });
    const user = userEvent.setup();
    mount();

    await user.click(await screen.findByRole('button', { name: /change email address/i }));
    await user.type(screen.getByLabelText(/new email address/i), 'taken@example.com');
    await user.click(screen.getByRole('button', { name: /send confirmation link/i }));

    expect(await screen.findByText(expected)).toBeInTheDocument();
  });

  it('stays silent when the user cancels the password prompt', async () => {
    // Cancelling is a deliberate choice. Showing an error would scold the user for
    // changing their mind.
    apiFetch.mockResolvedValueOnce(noPending);
    requireStepUp.mockRejectedValue(new ReauthCancelledError());
    const user = userEvent.setup();
    mount();

    await user.click(await screen.findByRole('button', { name: /change email address/i }));
    await user.type(screen.getByLabelText(/new email address/i), 'new@example.com');
    await user.click(screen.getByRole('button', { name: /send confirmation link/i }));

    await waitFor(() => expect(requireStepUp).toHaveBeenCalled());
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('keeps working when the pending lookup fails', async () => {
    // A failed banner read must not take the change form down with it.
    apiFetch.mockRejectedValue(new Error('offline'));
    mount();

    expect(
      await screen.findByRole('button', { name: /change email address/i }),
    ).toBeInTheDocument();
  });
});
