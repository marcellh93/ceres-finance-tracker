import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { toast } from 'sonner';
import { AccountRowMenu } from './AccountRowMenu';
import type { AccountListItemDto } from './accounts-api';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

let mockFetch: ReturnType<typeof vi.fn>;

const activeWithTransactions: AccountListItemDto = {
  id: 'a-1', name: 'Checking Account',
  accountTypeId: 1, accountTypeName: 'Asset',
  currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
  description: null, isActive: true, excludeFromSpendable: false,
  liabilityRepaymentType: null, interestRate: null,
  balance: 2114.56, hasTransactions: true,
};

const activeEmpty: AccountListItemDto = {
  ...activeWithTransactions, id: 'a-2', name: 'Cash', balance: 0, hasTransactions: false,
};

const archived: AccountListItemDto = {
  ...activeWithTransactions, id: 'a-3', name: 'Old Savings', isActive: false,
};

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
});

afterEach(() => vi.resetAllMocks());

function renderMenu(account: AccountListItemDto, onChanged = vi.fn()) {
  return render(
    <MemoryRouter initialEntries={['/accounts']}>
      <Routes>
        <Route path="/accounts" element={<AccountRowMenu account={account} onChanged={onChanged} />} />
        <Route path="/accounts/:id/edit" element={<div data-testid="edit-page">EDIT</div>} />
        <Route path="/accounts/:id/ledger" element={<div data-testid="ledger-page">LEDGER</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('AccountRowMenu', () => {
  it('shows Edit + View ledger + Archive for active rows', async () => {
    renderMenu(activeWithTransactions);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    expect(await screen.findByText('Edit')).toBeInTheDocument();
    expect(screen.getByText('View ledger')).toBeInTheDocument();
    expect(screen.getByText('Archive…')).toBeInTheDocument();
  });

  it('shows View ledger + Reactivate for archived rows', async () => {
    renderMenu(archived);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    expect(await screen.findByText('View ledger')).toBeInTheDocument();
    expect(screen.getByText('Reactivate')).toBeInTheDocument();
    expect(screen.queryByText('Edit')).toBeNull();
    expect(screen.queryByText('Archive…')).toBeNull();
  });

  it('clicking Edit navigates to /accounts/:id/edit', async () => {
    renderMenu(activeWithTransactions);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Edit'));
    expect(await screen.findByTestId('edit-page')).toBeInTheDocument();
  });

  it('clicking View ledger navigates to /accounts/:id/ledger', async () => {
    renderMenu(activeWithTransactions);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('View ledger'));
    expect(await screen.findByTestId('ledger-page')).toBeInTheDocument();
  });

  it('archive dialog uses safe copy for empty accounts', async () => {
    renderMenu(activeEmpty);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    expect(await screen.findByText(/archive 'cash'\?/i)).toBeInTheDocument();
    expect(screen.getByText(/no transactions/i)).toBeInTheDocument();
  });

  it('archive dialog hides the exclude-from-reports switch for empty accounts', async () => {
    renderMenu(activeEmpty);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    await screen.findByText(/archive 'cash'\?/i);
    // The switch only applies when there are transactions to count toward
    // net worth — empty accounts contribute nothing either way.
    expect(screen.queryByLabelText(/exclude from net worth/i)).toBeNull();
  });

  it('archive dialog uses consequence copy + exclude switch for accounts with transactions', async () => {
    renderMenu(activeWithTransactions);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    expect(await screen.findByText(/archive 'checking account'\?/i)).toBeInTheDocument();
    expect(screen.getByText(/still counts toward your net worth/i)).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: /exclude from net worth and reports/i })).toBeInTheDocument();
  });

  it('archive 204 fires toast.success, onChanged, and sends excludeFromReports=false by default', async () => {
    mockFetch.mockResolvedValue({ ok: true, status: 204, json: async () => null });
    const onChanged = vi.fn();
    renderMenu(activeWithTransactions, onChanged);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    fireEvent.click(await screen.findByRole('button', { name: 'Archive' }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Archived.'));
    expect(onChanged).toHaveBeenCalledTimes(1);
    const [, init] = mockFetch.mock.calls[0];
    expect(init.method).toBe('PATCH');
    expect(JSON.parse(init.body as string)).toEqual({ excludeFromReports: false });
  });

  it('archive sends excludeFromReports=true when the switch is toggled on', async () => {
    mockFetch.mockResolvedValue({ ok: true, status: 204, json: async () => null });
    renderMenu(activeWithTransactions);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    const sw = await screen.findByRole('switch', { name: /exclude from net worth and reports/i });
    fireEvent.click(sw);
    fireEvent.click(screen.getByRole('button', { name: 'Archive' }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Archived.'));
    const [, init] = mockFetch.mock.calls[0];
    expect(JSON.parse(init.body as string)).toEqual({ excludeFromReports: true });
  });

  it('reactivate 204 fires toast.success and onChanged', async () => {
    mockFetch.mockResolvedValue({ ok: true, status: 204, json: async () => null });
    const onChanged = vi.fn();
    renderMenu(archived, onChanged);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Reactivate'));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Reactivated.'));
    expect(onChanged).toHaveBeenCalledTimes(1);
  });

  it('archive non-2xx fires generic error toast', async () => {
    mockFetch.mockResolvedValue({ ok: false, status: 500, json: async () => null });
    renderMenu(activeWithTransactions);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive…'));
    fireEvent.click(await screen.findByRole('button', { name: 'Archive' }));
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Couldn't archive. Try again."),
    );
  });
});
