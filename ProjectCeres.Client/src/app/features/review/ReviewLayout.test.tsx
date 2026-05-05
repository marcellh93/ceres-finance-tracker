import { render, screen, waitFor } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ReviewCountProvider } from './ReviewCountProvider';
import { ReviewLayout } from './ReviewLayout';

function renderAt(initialEntry: string, recon: number, xfer: number, transferRows: unknown[] = []) {
  vi.stubGlobal('fetch', vi.fn((url: string) => {
    if (url.endsWith('/reconciliation-review/pending/count')) {
      return Promise.resolve({ ok: true, json: async () => recon } as Response);
    }
    if (url.endsWith('/transfer-review/pending/count')) {
      return Promise.resolve({ ok: true, json: async () => xfer } as Response);
    }
    if (url.endsWith('/reconciliation-review/pending')) {
      return Promise.resolve({ ok: true, json: async () => [] } as Response);
    }
    if (url.endsWith('/transfer-review/pending')) {
      return Promise.resolve({ ok: true, json: async () => transferRows } as Response);
    }
    if (url.endsWith('/api/accounts')) {
      return Promise.resolve({ ok: true, json: async () => [] } as Response);
    }
    return Promise.reject(new Error(`Unexpected URL: ${url}`));
  }) as typeof fetch);

  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <ReviewCountProvider>
        <Routes>
          <Route path="/review" element={<ReviewLayout />} />
        </Routes>
      </ReviewCountProvider>
    </MemoryRouter>,
  );
}

// Helper: assert which tab is active via base-ui's `data-active` attribute
// (set as empty string when active, absent otherwise).
function expectActiveTab(name: RegExp) {
  const tab = screen.getByRole('tab', { name });
  const isActive = tab.hasAttribute('data-active') || tab.getAttribute('aria-selected') === 'true';
  expect(isActive).toBe(true);
}

describe('ReviewLayout', () => {
  beforeEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('renders h1 and description', async () => {
    renderAt('/review', 0, 0);
    expect(screen.getByRole('heading', { name: /^Review$/ })).toBeInTheDocument();
    expect(screen.getByText(/Triage rows the importer staged for you/)).toBeInTheDocument();
  });

  it('both tab triggers always visible; badges hidden when count is 0', async () => {
    renderAt('/review', 0, 0);
    expect(screen.getByRole('tab', { name: /Reconciliations/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Transfers/ })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByTestId('reconciliations-count')).toBeNull();
      expect(screen.queryByTestId('transfers-count')).toBeNull();
    });
  });

  it('badges render when count > 0', async () => {
    renderAt('/review', 2, 1);
    await waitFor(() => expect(screen.getByTestId('reconciliations-count').textContent).toBe('2'));
    expect(screen.getByTestId('transfers-count').textContent).toBe('1');
  });

  it('?tab=transfers selects Transfers tab', async () => {
    renderAt('/review?tab=transfers', 0, 0);
    await waitFor(() => expectActiveTab(/Transfers/));
  });

  it('A3: no-param + reconciliations > 0 -> Reconciliations active', async () => {
    renderAt('/review', 1, 0);
    await waitFor(() => expectActiveTab(/Reconciliations/));
  });

  it('A3: no-param + reconciliations === 0 + transfers > 0 -> Transfers active after counts arrive', async () => {
    renderAt('/review', 0, 1);
    await waitFor(() => expectActiveTab(/Transfers/));
  });

  it('A3: no-param + both empty -> Reconciliations active', async () => {
    renderAt('/review', 0, 0);
    await waitFor(() => expectActiveTab(/Reconciliations/));
  });

  it('clicking Transfers selects the Transfers tab', async () => {
    renderAt('/review', 1, 0);
    await waitFor(() => expectActiveTab(/Reconciliations/));
    await userEvent.click(screen.getByRole('tab', { name: /Transfers/ }));
    await waitFor(() => expectActiveTab(/Transfers/));
  });

  it('Transfers fetch only fires after Transfers tab is selected (lazy mount)', async () => {
    const fetchMock = vi.fn((url: string) => {
      if (url.endsWith('/reconciliation-review/pending/count')) return Promise.resolve({ ok: true, json: async () => 1 } as Response);
      if (url.endsWith('/transfer-review/pending/count'))       return Promise.resolve({ ok: true, json: async () => 0 } as Response);
      if (url.endsWith('/reconciliation-review/pending'))       return Promise.resolve({ ok: true, json: async () => [] } as Response);
      if (url.endsWith('/transfer-review/pending'))             return Promise.resolve({ ok: true, json: async () => [] } as Response);
      if (url.endsWith('/api/accounts'))                        return Promise.resolve({ ok: true, json: async () => [] } as Response);
      return Promise.reject(new Error(`Unexpected URL: ${url}`));
    });
    vi.stubGlobal('fetch', fetchMock as typeof fetch);

    render(
      <MemoryRouter initialEntries={['/review']}>
        <ReviewCountProvider>
          <Routes>
            <Route path="/review" element={<ReviewLayout />} />
          </Routes>
        </ReviewCountProvider>
      </MemoryRouter>,
    );
    await waitFor(() => expectActiveTab(/Reconciliations/));
    expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith('/transfer-review/pending'))).toBe(false);

    await userEvent.click(screen.getByRole('tab', { name: /Transfers/ }));
    await waitFor(() => expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith('/transfer-review/pending'))).toBe(true));
  });

  it('first-paint: shows Reconciliations while counts loading; switches to Transfers once counts resolve 0/N', async () => {
    let resolveRecon!: (v: Response) => void;
    let resolveXfer!:  (v: Response) => void;

    vi.stubGlobal('fetch', vi.fn((url: string) => {
      if (url.endsWith('/reconciliation-review/pending/count')) {
        return new Promise<Response>((r) => { resolveRecon = r; });
      }
      if (url.endsWith('/transfer-review/pending/count')) {
        return new Promise<Response>((r) => { resolveXfer = r; });
      }
      if (url.endsWith('/reconciliation-review/pending')) return Promise.resolve({ ok: true, json: async () => [] } as Response);
      if (url.endsWith('/transfer-review/pending'))       return Promise.resolve({ ok: true, json: async () => [] } as Response);
      if (url.endsWith('/api/accounts'))                  return Promise.resolve({ ok: true, json: async () => [] } as Response);
      return Promise.reject(new Error(`Unexpected URL: ${url}`));
    }) as typeof fetch);

    render(
      <MemoryRouter initialEntries={['/review']}>
        <ReviewCountProvider>
          <Routes>
            <Route path="/review" element={<ReviewLayout />} />
          </Routes>
        </ReviewCountProvider>
      </MemoryRouter>,
    );
    // First paint: Reconciliations is provisionally active while counts are still loading.
    expectActiveTab(/Reconciliations/);

    // Resolve both counts: reconciliation 0, transfer 3.
    resolveRecon({ ok: true, json: async () => 0 } as Response);
    resolveXfer({ ok: true, json: async () => 3 } as Response);

    // After counts resolve, A3 should adjust to Transfers (since recon=0 and xfer>0).
    await waitFor(() => expectActiveTab(/Transfers/));
  });

  it('first-paint + user-touched: post-load adjustment is suppressed when user already clicked', async () => {
    let resolveRecon!: (v: Response) => void;
    let resolveXfer!:  (v: Response) => void;

    vi.stubGlobal('fetch', vi.fn((url: string) => {
      if (url.endsWith('/reconciliation-review/pending/count')) {
        return new Promise<Response>((r) => { resolveRecon = r; });
      }
      if (url.endsWith('/transfer-review/pending/count')) {
        return new Promise<Response>((r) => { resolveXfer = r; });
      }
      if (url.endsWith('/reconciliation-review/pending')) return Promise.resolve({ ok: true, json: async () => [] } as Response);
      if (url.endsWith('/transfer-review/pending'))       return Promise.resolve({ ok: true, json: async () => [] } as Response);
      if (url.endsWith('/api/accounts'))                  return Promise.resolve({ ok: true, json: async () => [] } as Response);
      return Promise.reject(new Error(`Unexpected URL: ${url}`));
    }) as typeof fetch);

    render(
      <MemoryRouter initialEntries={['/review']}>
        <ReviewCountProvider>
          <Routes>
            <Route path="/review" element={<ReviewLayout />} />
          </Routes>
        </ReviewCountProvider>
      </MemoryRouter>,
    );
    // User clicks Reconciliations explicitly. Even though it was already provisionally active,
    // this click sets the userTouchedTab.current ref, which must suppress the post-load adjustment.
    await userEvent.click(screen.getByRole('tab', { name: /Reconciliations/ }));

    // Resolve counts so that, without the userTouchedTab guard, A3 would switch to Transfers.
    resolveRecon({ ok: true, json: async () => 0 } as Response);
    resolveXfer({ ok: true, json: async () => 5 } as Response);

    // Wait long enough for any auto-switch effect to fire, then assert it did NOT happen.
    await waitFor(() => expect(screen.getByTestId('transfers-count').textContent).toBe('5'));
    // Reconciliations must STILL be active (the user-touched flag suppressed the auto-switch).
    expectActiveTab(/Reconciliations/);
  });
});
