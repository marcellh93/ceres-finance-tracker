import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { I18nextProvider } from 'react-i18next';
import { describe, expect, it, vi, beforeEach } from 'vitest';
import i18n from '../../i18n/i18n';
import { ErasureCancel } from './ErasureCancel';

const apiFetch = vi.fn();
vi.mock('../../lib/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../lib/api-client')>()),
  apiFetch: (...args: unknown[]) => apiFetch(...args),
}));

function mount(hash: string) {
  return render(
    <I18nextProvider i18n={i18n}>
      <MemoryRouter initialEntries={[`/erasure/cancel${hash}`]}>
        <Routes>
          <Route path="/erasure/cancel" element={<ErasureCancel />} />
        </Routes>
      </MemoryRouter>
    </I18nextProvider>,
  );
}

describe('ErasureCancel', () => {
  beforeEach(() => {
    apiFetch.mockReset();
  });

  it('reads the token from the URL fragment and posts it to the cancel endpoint', async () => {
    apiFetch.mockResolvedValue({ ok: true, status: 204 });

    mount('#token=cancel-tok');

    await waitFor(() => expect(apiFetch).toHaveBeenCalledTimes(1));
    expect(apiFetch).toHaveBeenCalledWith(
      '/api/profile/erasure/cancel',
      expect.objectContaining({ method: 'POST', body: { token: 'cancel-tok' } }),
    );
  });

  it('on success (204), confirms the account is safe and offers sign-in', async () => {
    apiFetch.mockResolvedValue({ ok: true, status: 204 });

    mount('#token=cancel-tok');

    expect(await screen.findByText(/erasure cancelled/i)).toBeInTheDocument();
    expect(screen.getByText(/no longer scheduled for erasure/i)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /go to sign in/i })).toBeInTheDocument();
  });

  it('announces the success state to screen readers via role=status', async () => {
    // Regression check: the reassurance body text ("Nothing was deleted...")
    // must be announced automatically on this auto-firing, unauthenticated
    // page — a screen-reader user who lands here from the email link has no
    // other cue that the outcome is safe. The "verifying" interim state also
    // carries role=status, so wait for the actual success copy specifically
    // rather than the first status-role element to appear.
    apiFetch.mockResolvedValue({ ok: true, status: 204 });

    mount('#token=cancel-tok');

    const status = await waitFor(() => {
      const el = screen.getByRole('status');
      expect(el).toHaveTextContent(/erasure cancelled/i);
      return el;
    });
    expect(status).toHaveTextContent(/no longer scheduled for erasure/i);
  });

  it('moves focus to the outcome heading on mount, per the route-change focus convention', async () => {
    apiFetch.mockResolvedValue({ ok: true, status: 204 });

    mount('#token=cancel-tok');

    const heading = await screen.findByRole('heading', { name: /erasure cancelled/i });
    await waitFor(() => expect(heading).toHaveFocus());
  });

  it('on 410 (already used/completed), gives the "gone" message, not "invalid"', async () => {
    // The load-bearing assertion. A 410 means the request already resolved one
    // way or another (cancelled already, or the erasure already ran) — telling
    // the user "invalid link" instead would hide that the account may already
    // be gone, which is exactly the outcome this page exists to help with.
    apiFetch.mockResolvedValue({ ok: false, status: 410, code: 'ERASURE_LINK_EXPIRED', message: '' });

    mount('#token=used-already');

    expect(await screen.findByText(/already been used/i)).toBeInTheDocument();
    expect(screen.getByText(/cannot bring it back/i)).toBeInTheDocument();
  });

  it('on 404 (unknown token), gives the "invalid" message, distinct from "gone"', async () => {
    apiFetch.mockResolvedValue({ ok: false, status: 404, code: 'HTTP_404', message: '' });

    mount('#token=never-existed');

    expect(await screen.findByText(/cancellation link is invalid/i)).toBeInTheDocument();
    expect(screen.queryByText(/already been used/i)).not.toBeInTheDocument();
  });

  it('never calls the server when the link carries no token', async () => {
    mount('');

    expect(await screen.findByText(/cancellation link is invalid/i)).toBeInTheDocument();
    expect(apiFetch).not.toHaveBeenCalled();
  });
});
