import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AccountsLayout } from './AccountsLayout';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

let mockFetch: ReturnType<typeof vi.fn>;

const allRows = [
  { id: 'a-1', name: 'Cash',             accountTypeId: 1, accountTypeName: 'Asset',     currencyId: 1, currencyCode: 'EUR', currencySymbol: '€', description: null, isActive: true,  excludeFromSpendable: false, liabilityRepaymentType: null,         interestRate: null,  balance: 120,     hasTransactions: false },
  { id: 'a-2', name: 'Checking Account', accountTypeId: 1, accountTypeName: 'Asset',     currencyId: 1, currencyCode: 'EUR', currencySymbol: '€', description: null, isActive: true,  excludeFromSpendable: false, liabilityRepaymentType: null,         interestRate: null,  balance: 2114.56, hasTransactions: true  },
  { id: 'a-3', name: 'Credit Card',      accountTypeId: 2, accountTypeName: 'Liability', currencyId: 1, currencyCode: 'EUR', currencySymbol: '€', description: null, isActive: true,  excludeFromSpendable: false, liabilityRepaymentType: 'FullMonthly', interestRate: null,  balance: 500,     hasTransactions: true  },
  { id: 'a-4', name: 'Old Savings',      accountTypeId: 1, accountTypeName: 'Asset',     currencyId: 1, currencyCode: 'EUR', currencySymbol: '€', description: null, isActive: false, excludeFromSpendable: false, liabilityRepaymentType: null,         interestRate: null,  balance: 0,       hasTransactions: true  },
];

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url === '/api/accounts') {
      return Promise.resolve({ ok: true, status: 200, json: async () => allRows.filter((r) => r.isActive) });
    }
    if (url === '/api/accounts?includeInactive=true') {
      return Promise.resolve({ ok: true, status: 200, json: async () => allRows });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

afterEach(() => vi.resetAllMocks());

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/accounts" element={<AccountsLayout />}>
          <Route path="new" element={<div data-testid="new-page">NEW</div>} />
          <Route path=":id/edit" element={<div data-testid="edit-page">EDIT</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('AccountsLayout', () => {
  it('renders skeleton after the delay window when loading is slow', async () => {
    mockFetch.mockImplementation(() => new Promise(() => {}));
    renderAt('/accounts');
    expect(screen.queryByTestId('accounts-skeleton')).toBeNull();
    await waitFor(
      () => expect(screen.getByTestId('accounts-skeleton')).toBeInTheDocument(),
      { timeout: 500 },
    );
  });

  it('renders active accounts in the table', async () => {
    renderAt('/accounts');
    await screen.findByText('Cash');
    expect(screen.getByText('Checking Account')).toBeInTheDocument();
    expect(screen.getByText('Credit Card')).toBeInTheDocument();
    expect(screen.queryByText('Old Savings')).toBeNull();
  });

  it('search filters rows', async () => {
    renderAt('/accounts');
    await screen.findByText('Cash');
    fireEvent.change(screen.getByPlaceholderText(/filter accounts/i), { target: { value: 'che' } });
    await waitFor(() => {
      expect(screen.getByText('Checking Account')).toBeInTheDocument();
      expect(screen.queryByText('Cash')).toBeNull();
    });
  });

  it('search-empty state with Clear search', async () => {
    renderAt('/accounts');
    await screen.findByText('Cash');
    fireEvent.change(screen.getByPlaceholderText(/filter accounts/i), { target: { value: 'zzznomatch' } });
    await waitFor(() => {
      expect(screen.getByText(/no accounts match/i)).toBeInTheDocument();
    });
    expect(screen.getByRole('button', { name: /clear search/i })).toBeInTheDocument();
  });

  it('Include archived toggle adds archived rows', async () => {
    renderAt('/accounts');
    await screen.findByText('Cash');
    fireEvent.click(screen.getByRole('switch', { name: /include archived/i }));
    await screen.findByText('Old Savings');
  });

  it('archived rows render Archived badge', async () => {
    renderAt('/accounts?includeInactive=true');
    const old = (await screen.findByText('Old Savings')).closest('tr')!;
    expect(within(old).getByText('Archived')).toBeInTheDocument();
  });

  it('GET error renders CardError with Retry', async () => {
    mockFetch.mockImplementation(() =>
      Promise.resolve({ ok: false, status: 500, json: async () => null }),
    );
    renderAt('/accounts');
    await waitFor(() => expect(screen.getByText(/Accounts/)).toBeInTheDocument());
    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
  });

  it('clicking + New account navigates to /accounts/new', async () => {
    renderAt('/accounts');
    await screen.findByText('Cash');
    fireEvent.click(screen.getByRole('button', { name: /new account/i }));
    expect(await screen.findByTestId('new-page')).toBeInTheDocument();
  });

  it('renders the per-currency subtotal strip when accounts span 2+ currencies', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts') {
        return Promise.resolve({
          ok: true, status: 200,
          json: async () => [
            { ...allRows[0], id: 'eur', currencyCode: 'EUR', currencySymbol: '€', balance: 1000 },
            { ...allRows[0], id: 'usd', currencyCode: 'USD', currencySymbol: '$', balance: 500 },
          ],
        });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderAt('/accounts');
    // The strip shows the currency code labels alongside the totals; the table
    // cells don't render the code (only the symbol). So scoping by code text is enough.
    await screen.findByText(/EUR/);
    expect(screen.getByText(/USD/)).toBeInTheDocument();
  });

  it('hides the per-currency subtotal strip when only one currency', async () => {
    renderAt('/accounts');
    await screen.findByText('Cash');
    expect(screen.queryByText(/EUR €/)).toBeNull();
  });

  it('first-run empty state renders when no accounts at all', async () => {
    mockFetch.mockImplementation(() =>
      Promise.resolve({ ok: true, status: 200, json: async () => [] }),
    );
    renderAt('/accounts');
    await waitFor(() => {
      expect(screen.getByText(/no accounts yet/i)).toBeInTheDocument();
    });
    expect(screen.getByText(/create your first account/i)).toBeInTheDocument();
  });

  it('"No accounts." renders when only archived exist (toggle off)', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts') {
        return Promise.resolve({ ok: true, status: 200, json: async () => [] });
      }
      if (url === '/api/accounts?includeInactive=true') {
        return Promise.resolve({ ok: true, status: 200, json: async () => [allRows[3]] });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderAt('/accounts');
    await waitFor(() => {
      expect(screen.getByText(/^no accounts\.$/i)).toBeInTheDocument();
    });
  });

  it('does not flash the skeleton when the response is faster than the delay window', async () => {
    renderAt('/accounts');
    expect(screen.queryByTestId('accounts-skeleton')).toBeNull();
    await screen.findByText('Cash');
    expect(screen.queryByTestId('accounts-skeleton')).toBeNull();
  });

  it('shows the skeleton then cross-fades to data when the response is slow', async () => {
    let resolveFetch: (value: { ok: true; status: 200; json: () => Promise<unknown> }) => void;
    const slowResponse = new Promise<{ ok: true; status: 200; json: () => Promise<unknown> }>(
      (resolve) => { resolveFetch = resolve; },
    );
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts') return slowResponse;
      return Promise.resolve({ ok: true, status: 200, json: async () => [] });
    });

    renderAt('/accounts');

    await waitFor(
      () => expect(screen.getByTestId('accounts-skeleton')).toBeInTheDocument(),
      { timeout: 500 },
    );

    resolveFetch!({
      ok: true,
      status: 200,
      json: async () => allRows.filter((r) => r.isActive),
    });

    await screen.findByText('Cash');
  });

  describe('with prefers-reduced-motion', () => {
    const originalMatchMedia = window.matchMedia;

    afterEach(() => {
      Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        writable: true,
        value: originalMatchMedia,
      });
    });

    it('disables the cross-fade when prefers-reduced-motion matches', async () => {
      const matchMediaSpy = vi.fn().mockImplementation((query: string) => ({
        matches: query === '(prefers-reduced-motion: reduce)',
        media: query,
        onchange: null,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        addListener: vi.fn(),
        removeListener: vi.fn(),
        dispatchEvent: vi.fn(),
      }));
      Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        writable: true,
        value: matchMediaSpy,
      });

      renderAt('/accounts');
      await screen.findByText('Cash');
      const transitions = document.querySelectorAll('[data-data-transition]');
      expect(transitions.length).toBeGreaterThan(0);
      transitions.forEach((node) => {
        expect(node.getAttribute('data-reduced-motion')).toBe('true');
      });
    });
  });
});
