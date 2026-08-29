import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import i18n from '../../i18n/i18n';
import { EmailChangeConfirm } from './EmailChangeConfirm';

const apiFetch = vi.fn();
vi.mock('../../lib/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../lib/api-client')>()),
  apiFetch: (...args: unknown[]) => apiFetch(...args),
}));

function mount(hash: string) {
  return render(
    <I18nextProvider i18n={i18n}>
      <MemoryRouter initialEntries={[`/email-change/confirm${hash}`]}>
        <Routes>
          <Route path="/email-change/confirm" element={<EmailChangeConfirm />} />
        </Routes>
      </MemoryRouter>
    </I18nextProvider>,
  );
}

describe('EmailChangeConfirm', () => {
  beforeEach(() => {
    apiFetch.mockReset();
  });

  it('reads the token from the URL fragment, not the query string', async () => {
    // The server puts the token after '#' precisely so it never reaches a server
    // log or a referrer header. Reading it from the query string would be a
    // different, leakier contract — this pins which one we implement.
    apiFetch.mockResolvedValue({ ok: true });

    mount('#token=abc123');

    await waitFor(() => expect(apiFetch).toHaveBeenCalledTimes(1));
    expect(apiFetch).toHaveBeenCalledWith(
      '/api/auth/email-change/confirm',
      expect.objectContaining({ method: 'POST', body: { token: 'abc123' } }),
    );
  });

  it('shows success once the server confirms', async () => {
    apiFetch.mockResolvedValue({ ok: true });

    mount('#token=abc123');

    expect(await screen.findByText(/email address changed/i)).toBeInTheDocument();
  });

  it('never calls the server when the link carries no token', async () => {
    mount('');

    expect(await screen.findByText(/no longer valid/i)).toBeInTheDocument();
    // A tokenless request would be a pointless round-trip that also burns the
    // endpoint's per-IP rate-limit budget for a user who cannot succeed.
    expect(apiFetch).not.toHaveBeenCalled();
  });

  it('distinguishes a taken address from an expired link', async () => {
    // Different causes, different user actions: "taken" means start over with a
    // different address; "expired" means start over with the same one. Collapsing
    // them into one message tells the user to do the wrong thing half the time.
    apiFetch.mockResolvedValue({ ok: false, code: 'EMAIL_ALREADY_IN_USE' });

    mount('#token=abc123');

    expect(await screen.findByText(/that address is taken/i)).toBeInTheDocument();
    expect(screen.queryByText(/links last 30 minutes/i)).toBeNull();
  });

  it('treats a network failure as an invalid link rather than crashing', async () => {
    apiFetch.mockRejectedValue(new Error('offline'));

    mount('#token=abc123');

    expect(await screen.findByText(/no longer valid/i)).toBeInTheDocument();
  });
});
