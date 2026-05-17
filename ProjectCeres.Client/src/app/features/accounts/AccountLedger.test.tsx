import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { AccountLedger } from './AccountLedger';

let mockFetch: ReturnType<typeof vi.fn>;

const baseAccount = {
  id: 'a-1', name: 'Checking Account',
  accountTypeId: 1, accountTypeName: 'Asset',
  currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
  description: null, isActive: true, excludeFromSpendable: false,
  liabilityRepaymentType: null, interestRate: null,
  openingBalance: 1000, openingBalanceDate: '2026-01-01',
};

const baseLedger = {
  accountId: 'a-1',
  accountName: 'Checking Account',
  currencySymbol: '€',
  entries: [
    { date: '2026-01-01', createdAt: '2026-01-01T00:00:00Z', description: 'Opening Balance', entryType: 'OpeningBalance', categoryName: null, signedAmount: 1000, runningBalance: 1000 },
    { date: '2026-01-15', createdAt: '2026-01-15T10:00:00Z', description: 'Salary',          entryType: 'Transaction',     categoryName: 'Salary', signedAmount: 2500, runningBalance: 3500 },
    { date: '2026-01-20', createdAt: '2026-01-20T12:00:00Z', description: 'Groceries',        entryType: 'Transaction',     categoryName: 'Groceries', signedAmount: -85, runningBalance: 3415 },
  ],
};

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url === '/api/accounts/a-1') return Promise.resolve({ ok: true, status: 200, json: async () => baseAccount });
    if (url === '/api/accounts/a-1/ledger') return Promise.resolve({ ok: true, status: 200, json: async () => baseLedger });
    if (url === '/api/accounts/a-missing') return Promise.resolve({ ok: false, status: 404, json: async () => null });
    if (url === '/api/accounts/a-missing/ledger') return Promise.resolve({ ok: false, status: 404, json: async () => null });
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/accounts" element={<div data-testid="list-page">LIST</div>} />
        <Route path="/accounts/:id/ledger" element={<AccountLedger />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('AccountLedger', () => {
  it('renders the header with account name', async () => {
    renderAt('/accounts/a-1/ledger');
    await screen.findByText(/Checking Account — Ledger/i);
  });

  it('renders one row per ledger entry', async () => {
    renderAt('/accounts/a-1/ledger');
    await screen.findAllByText('Salary');
    expect(screen.getAllByText('Groceries').length).toBeGreaterThan(0);
    expect(screen.getByText('Opening Balance')).toBeInTheDocument();
  });

  it('Amount column color follows sign', async () => {
    renderAt('/accounts/a-1/ledger');
    await screen.findAllByText('Salary');
    const groceriesRow = screen.getAllByText('Groceries')[0].closest('tr')!;
    const cells = groceriesRow.querySelectorAll('td');
    const amountCell = cells[3];
    expect(amountCell.className).toMatch(/text-destructive/);
    const salaryRow = screen.getAllByText('Salary')[0].closest('tr')!;
    const salaryAmount = salaryRow.querySelectorAll('td')[3];
    expect(salaryAmount.className).not.toMatch(/text-destructive/);
  });

  it('Running balance column color follows account-type convention (Asset normal here)', async () => {
    renderAt('/accounts/a-1/ledger');
    await screen.findAllByText('Salary');
    const salaryRow = screen.getAllByText('Salary')[0].closest('tr')!;
    const runningCell = salaryRow.querySelectorAll('td')[4];
    expect(runningCell.className).not.toMatch(/text-destructive/);
  });

  it('renders Archived badge when account is archived', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts/a-1') return Promise.resolve({ ok: true, status: 200, json: async () => ({ ...baseAccount, isActive: false }) });
      if (url === '/api/accounts/a-1/ledger') return Promise.resolve({ ok: true, status: 200, json: async () => baseLedger });
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderAt('/accounts/a-1/ledger');
    await screen.findByText(/Checking Account — Ledger/i);
    expect(screen.getByText('Archived')).toBeInTheDocument();
  });

  it('renders empty state when there are no entries', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts/a-1') return Promise.resolve({ ok: true, status: 200, json: async () => baseAccount });
      if (url === '/api/accounts/a-1/ledger') return Promise.resolve({ ok: true, status: 200, json: async () => ({ ...baseLedger, entries: [] }) });
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderAt('/accounts/a-1/ledger');
    await waitFor(() => expect(screen.getByText(/no entries found/i)).toBeInTheDocument());
  });

  it('GET 404 renders the not-found banner', async () => {
    renderAt('/accounts/a-missing/ledger');
    await waitFor(() => expect(screen.getByText(/that account doesn't exist/i)).toBeInTheDocument());
  });

  it('Projection card is hidden for Asset accounts', async () => {
    renderAt('/accounts/a-1/ledger');
    await screen.findAllByText('Salary');
    expect(screen.queryByText(/payoff projection/i)).toBeNull();
  });

  it('Projection card renders for Liability + Amortising + InterestRate', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts/a-1') {
        return Promise.resolve({
          ok: true, status: 200,
          json: async () => ({
            ...baseAccount, accountTypeId: 2, accountTypeName: 'Liability',
            liabilityRepaymentType: 'Amortising', interestRate: 0.035,
          }),
        });
      }
      if (url === '/api/accounts/a-1/ledger') return Promise.resolve({ ok: true, status: 200, json: async () => ({ ...baseLedger, entries: [{ ...baseLedger.entries[0], runningBalance: 5000 }] }) });
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderAt('/accounts/a-1/ledger');
    await screen.findByText(/payoff projection/i);
    expect(screen.getByLabelText(/monthly payment/i)).toBeInTheDocument();
  });

  it('Projection card hidden for Liability + FullMonthly', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts/a-1') {
        return Promise.resolve({
          ok: true, status: 200,
          json: async () => ({
            ...baseAccount, accountTypeId: 2, accountTypeName: 'Liability',
            liabilityRepaymentType: 'FullMonthly', interestRate: null,
          }),
        });
      }
      if (url === '/api/accounts/a-1/ledger') return Promise.resolve({ ok: true, status: 200, json: async () => baseLedger });
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderAt('/accounts/a-1/ledger');
    await screen.findByText(/Checking Account — Ledger/i);
    expect(screen.queryByText(/payoff projection/i)).toBeNull();
  });

  it('Calculate computes payoff months from balance/rate/payment', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts/a-1') {
        return Promise.resolve({
          ok: true, status: 200,
          json: async () => ({
            ...baseAccount, accountTypeId: 2, accountTypeName: 'Liability',
            liabilityRepaymentType: 'Amortising', interestRate: 0.035,
          }),
        });
      }
      if (url === '/api/accounts/a-1/ledger') return Promise.resolve({ ok: true, status: 200, json: async () => ({ ...baseLedger, entries: [{ ...baseLedger.entries[0], runningBalance: 5000 }] }) });
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });
    renderAt('/accounts/a-1/ledger');
    await screen.findByLabelText(/monthly payment/i);
    fireEvent.change(screen.getByLabelText(/monthly payment/i), { target: { value: '500' } });
    fireEvent.click(screen.getByRole('button', { name: /calculate/i }));
    await waitFor(() => expect(screen.getByText(/estimated payoff/i)).toBeInTheDocument());
    expect(screen.getByText(/months/i)).toBeInTheDocument();
  });
});
