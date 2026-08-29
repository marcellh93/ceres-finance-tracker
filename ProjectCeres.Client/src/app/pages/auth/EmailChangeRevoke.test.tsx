import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import i18n from '../../i18n/i18n';
import { EmailChangeRevoke } from './EmailChangeRevoke';

const apiFetch = vi.fn();
vi.mock('../../lib/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../lib/api-client')>()),
  apiFetch: (...args: unknown[]) => apiFetch(...args),
}));

function mount(hash: string) {
  return render(
    <I18nextProvider i18n={i18n}>
      <MemoryRouter initialEntries={[`/email-change/revoke${hash}`]}>
        <Routes>
          <Route path="/email-change/revoke" element={<EmailChangeRevoke />} />
        </Routes>
      </MemoryRouter>
    </I18nextProvider>,
  );
}

describe('EmailChangeRevoke', () => {
  beforeEach(() => {
    apiFetch.mockReset();
  });

  it('reads the token from the URL fragment', async () => {
    apiFetch.mockResolvedValue({ ok: true });

    mount('#token=revoke-tok');

    await waitFor(() => expect(apiFetch).toHaveBeenCalledTimes(1));
    expect(apiFetch).toHaveBeenCalledWith(
      '/api/auth/email-change/revoke',
      expect.objectContaining({ method: 'POST', body: { token: 'revoke-tok' } }),
    );
  });

  it('on success, says the address is unchanged and offers a password change', async () => {
    // This link is reached by someone who may have just learned their account is
    // being moved without their consent. Cancelling the change is necessary but
    // not sufficient — whoever started it still has the session.
    apiFetch.mockResolvedValue({ ok: true });

    mount('#token=revoke-tok');

    expect(await screen.findByText(/email change cancelled/i)).toBeInTheDocument();
    expect(screen.getByText(/your address is unchanged/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /change your password/i })).toBeInTheDocument();
  });

  it('on a dead link, allows that the change may ALREADY have completed', async () => {
    // The load-bearing assertion of this file. The revoke link lives 7 days but the
    // confirm link only 30 minutes, so a dead revoke link routinely means the change
    // already went through — and RevokeAsync cannot undo a completed change. Saying
    // only "invalid link" would leave the person this link exists to protect with no
    // idea what happened. If someone later simplifies this copy to a bare
    // invalid-link message, this test must fail.
    apiFetch.mockResolvedValue({ ok: false, code: 'INVALID_EMAIL_CHANGE_TOKEN' });

    mount('#token=stale');

    expect(await screen.findByText(/no longer valid/i)).toBeInTheDocument();
    expect(screen.getByText(/went through before you got here/i)).toBeInTheDocument();
  });

  it('on a dead link, gives both recovery steps in order', async () => {
    // Password reset first (locks the attacker out), support second (recovers the
    // address). The order matters: contacting support first leaves the session live.
    apiFetch.mockResolvedValue({ ok: false, code: 'INVALID_EMAIL_CHANGE_TOKEN' });

    mount('#token=stale');

    const steps = await screen.findAllByRole('listitem');
    expect(steps).toHaveLength(2);
    expect(steps[0]).toHaveTextContent(/reset your password/i);
    expect(steps[1]).toHaveTextContent(/contact support/i);
  });

  it('never calls the server when the link carries no token', async () => {
    mount('');

    expect(await screen.findByText(/no longer valid/i)).toBeInTheDocument();
    expect(apiFetch).not.toHaveBeenCalled();
  });
});
