import { render, screen, waitFor, within } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SessionsPage } from './SessionsPage';
import type { SessionDto } from './sessions-api';

const logout = vi.fn();
const navigate = vi.fn();

vi.mock('../../auth/auth-context', () => ({
  useAuth: () => ({ logout, user: { twoFactorEnabled: false } }),
}));

vi.mock('react-router-dom', async (importOriginal) => ({
  ...(await importOriginal<typeof import('react-router-dom')>()),
  useNavigate: () => navigate,
}));

// requireStepUp is exercised in its own suite. Here it is a pass-through so
// these tests assert what the PAGE does; the reauth-specific test below
// overrides it to prove the page routes 401s into the dialog rather than
// treating them as a sign-out.
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

function session(over: Partial<SessionDto> = {}): SessionDto {
  return {
    id: 'aaaaaaaa-0000-0000-0000-000000000001',
    createdAt: '2026-08-20T09:00:00Z',
    lastUsedAt: '2026-08-23T07:30:00Z',
    ipCreatedAt: '81.61.2.9',
    userAgent:
      'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36',
    isCurrent: false,
    ...over,
  };
}

function renderPage() {
  return render(
    <MemoryRouter>
      <SessionsPage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  requireStepUp.mockImplementation(<T,>(action: () => Promise<T>) => action());
});

describe('SessionsPage', () => {
  it('lists sessions with a summarized user agent, not the raw header', async () => {
    apiFetch.mockResolvedValue({ ok: true, status: 200, data: [session()] });
    renderPage();

    expect(await screen.findByText('Chrome on macOS')).toBeInTheDocument();
    expect(screen.queryByText(/AppleWebKit/)).not.toBeInTheDocument();
    expect(screen.getByText(/81\.61\.2\.9/)).toBeInTheDocument();
  });

  it('marks the current session and offers sign-out rather than revoke', async () => {
    apiFetch.mockResolvedValue({
      ok: true,
      status: 200,
      data: [session({ isCurrent: true }), session({ id: 'other', isCurrent: false })],
    });
    renderPage();

    expect(await screen.findByText('This device')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Revoke' })).toBeInTheDocument();
  });

  // Found by the 375px evidence screenshot: the card title used `?? 0`, so it
  // announced "0 active sessions" while the list was still loading — directly
  // contradicting the body beside it, which correctly waited for data.
  it('does not claim zero sessions while still loading', async () => {
    let resolve!: (v: unknown) => void;
    apiFetch.mockReturnValue(new Promise((r) => { resolve = r; }));
    renderPage();

    expect(screen.queryByText('0 active sessions')).not.toBeInTheDocument();

    resolve({ ok: true, status: 200, data: [session()] });
    expect(await screen.findByText('1 active session')).toBeInTheDocument();
  });

  it('shows an empty state when no sessions come back', async () => {
    apiFetch.mockResolvedValue({ ok: true, status: 200, data: [] });
    renderPage();

    expect(await screen.findByText('No other active sessions.')).toBeInTheDocument();
  });

  it('revokes another session after confirmation and refetches the list', async () => {
    const user = userEvent.setup();
    apiFetch
      .mockResolvedValueOnce({ ok: true, status: 200, data: [session()] })
      .mockResolvedValueOnce({ ok: true, status: 204, data: null })
      .mockResolvedValueOnce({ ok: true, status: 200, data: [] });

    renderPage();
    await user.click(await screen.findByRole('button', { name: 'Revoke' }));
    // Confirm inside the dialog specifically — the row button carries the same
    // label, so an unscoped query could satisfy this test by clicking the row
    // twice and never opening the dialog at all.
    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByText(/Chrome on macOS will be signed out/)).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'Revoke' }));

    await waitFor(() => {
      expect(apiFetch).toHaveBeenCalledWith(
        '/api/sessions/aaaaaaaa-0000-0000-0000-000000000001',
        expect.objectContaining({ method: 'DELETE' }),
      );
    });
    // Third call is the refetch — the list on screen may be minutes stale
    // after a mid-action reauth, so splicing locally would be wrong.
    await waitFor(() => expect(apiFetch).toHaveBeenCalledTimes(3));
    expect(logout).not.toHaveBeenCalled();
  });

  it('logs out and redirects when the current session is revoked', async () => {
    const user = userEvent.setup();
    apiFetch
      .mockResolvedValueOnce({ ok: true, status: 200, data: [session({ isCurrent: true })] })
      .mockResolvedValueOnce({ ok: true, status: 204, data: null });

    renderPage();
    await user.click(await screen.findByRole('button', { name: 'Sign out' }));
    const dialog = await screen.findByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Sign out' }));

    await waitFor(() => expect(logout).toHaveBeenCalled());
    expect(navigate).toHaveBeenCalledWith('/login');
  });

  // The load-bearing one. These endpoints answer 401 REAUTH_REQUIRED, and the
  // project's usual useApi hook treats every 401 as a sign-out — using it here
  // would log the user out on arrival. This pins that the page routes the
  // request through requireStepUp instead.
  it('routes the initial fetch through the step-up wrapper', async () => {
    apiFetch.mockResolvedValue({ ok: true, status: 200, data: [] });
    renderPage();

    await waitFor(() => expect(requireStepUp).toHaveBeenCalled());
    expect(logout).not.toHaveBeenCalled();
  });

  it('shows a recoverable message when the user cancels the reauth prompt', async () => {
    const { ReauthCancelledError } = await import('../../auth/use-step-up');
    requireStepUp.mockRejectedValue(new ReauthCancelledError());
    renderPage();

    expect(
      await screen.findByText('Confirm your identity to view active sessions.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });
});
