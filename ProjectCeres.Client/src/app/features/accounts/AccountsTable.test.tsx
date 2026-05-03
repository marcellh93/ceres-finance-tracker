import { render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { AccountsTable } from './AccountsTable';
import type { AccountListItemDto } from './accounts-api';

const rows: AccountListItemDto[] = [
  {
    id: 'a-1', name: 'Cash', accountTypeId: 1, accountTypeName: 'Asset',
    currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
    description: null, isActive: true, excludeFromSpendable: false,
    liabilityRepaymentType: null, interestRate: null,
    balance: 120, hasTransactions: false,
  },
  {
    id: 'a-2', name: 'Checking Account', accountTypeId: 1, accountTypeName: 'Asset',
    currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
    description: null, isActive: true, excludeFromSpendable: false,
    liabilityRepaymentType: null, interestRate: null,
    balance: 2114.56, hasTransactions: true,
  },
  {
    id: 'a-3', name: 'Credit Card', accountTypeId: 2, accountTypeName: 'Liability',
    currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
    description: null, isActive: true, excludeFromSpendable: false,
    liabilityRepaymentType: 'FullMonthly', interestRate: null,
    balance: 500, hasTransactions: true,
  },
  {
    id: 'a-4', name: 'Old Savings', accountTypeId: 1, accountTypeName: 'Asset',
    currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
    description: null, isActive: false, excludeFromSpendable: false,
    liabilityRepaymentType: null, interestRate: null,
    balance: 0, hasTransactions: true,
  },
];

function renderTable(props?: Partial<React.ComponentProps<typeof AccountsTable>>) {
  return render(
    <MemoryRouter>
      <AccountsTable rows={rows} onChanged={vi.fn()} {...props} />
    </MemoryRouter>,
  );
}

describe('AccountsTable', () => {
  it('renders one row per account', () => {
    renderTable();
    expect(screen.getByText('Cash')).toBeInTheDocument();
    expect(screen.getByText('Checking Account')).toBeInTheDocument();
    expect(screen.getByText('Credit Card')).toBeInTheDocument();
  });

  it('shows the Type cell for each row', () => {
    renderTable();
    const checkingRow = screen.getByText('Checking Account').closest('tr')!;
    expect(within(checkingRow).getByText('Asset')).toBeInTheDocument();
    const ccRow = screen.getByText('Credit Card').closest('tr')!;
    expect(within(ccRow).getByText('Liability')).toBeInTheDocument();
  });

  it('renders Liability balances in destructive color', () => {
    renderTable();
    const ccRow = screen.getByText('Credit Card').closest('tr')!;
    const balanceCell = within(ccRow).getByText(/500/);
    expect(balanceCell.className).toMatch(/text-destructive/);
  });

  it('renders Asset balances in normal foreground (no destructive)', () => {
    renderTable();
    const checkingRow = screen.getByText('Checking Account').closest('tr')!;
    const balanceCell = within(checkingRow).getByText(/2.114/);
    expect(balanceCell.className).not.toMatch(/text-destructive/);
  });

  it('shows the Archived badge on rows where isActive=false', () => {
    renderTable();
    const archivedRow = screen.getByText('Old Savings').closest('tr')!;
    expect(within(archivedRow).getByText('Archived')).toBeInTheDocument();
  });

  it('archived rows have opacity-60 class', () => {
    renderTable();
    const archivedRow = screen.getByText('Old Savings').closest('tr')!;
    expect(archivedRow.className).toContain('opacity-60');
  });

  it('renders a row-menu trigger for active rows', () => {
    renderTable();
    const cashRow = screen.getByText('Cash').closest('tr')!;
    expect(within(cashRow).getByRole('button', { name: /row actions/i })).toBeInTheDocument();
  });

  it('renders a row-menu trigger for archived rows too (View ledger only)', () => {
    renderTable();
    const archivedRow = screen.getByText('Old Savings').closest('tr')!;
    expect(within(archivedRow).getByRole('button', { name: /row actions/i })).toBeInTheDocument();
  });

  it('balance cells use tabular-nums', () => {
    renderTable();
    const cashRow = screen.getByText('Cash').closest('tr')!;
    const cells = within(cashRow).getAllByRole('cell');
    const balanceCell = cells[2];
    expect(balanceCell.className).toMatch(/tabular-nums/);
  });

  it('renders the empty state when rows array is empty', () => {
    renderTable({ rows: [] });
    expect(screen.getByText(/no accounts/i)).toBeInTheDocument();
  });
});
