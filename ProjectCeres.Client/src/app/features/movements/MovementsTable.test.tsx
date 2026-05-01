import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { MovementsTable } from './MovementsTable';
import type { MovementListItemDto } from './movements-api';

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const items: MovementListItemDto[] = [
  {
    id: 't1', movementType: 'Transaction', date: '2026-04-15', amount: 25.5,
    currencyCode: 'EUR', currencySymbol: '€', description: 'Rent', isCleared: true,
    accountName: 'Checking', categoryName: 'Housing', categoryTypeName: 'Expense',
    sourceAccountName: null, destAccountName: null, assetAccountName: null, liabilityAccountName: null,
  },
  {
    id: 't2', movementType: 'Transfer', date: '2026-04-14', amount: 100,
    currencyCode: 'EUR', currencySymbol: '€', description: 'Move savings', isCleared: false,
    accountName: null, categoryName: null, categoryTypeName: null,
    sourceAccountName: 'Checking', destAccountName: 'Savings', assetAccountName: null, liabilityAccountName: null,
  },
  {
    id: 'lp1', movementType: 'LiabilityPayment', date: '2026-04-13', amount: 50,
    currencyCode: 'EUR', currencySymbol: '€', description: null, isCleared: false,
    accountName: null, categoryName: null, categoryTypeName: null,
    sourceAccountName: null, destAccountName: null, assetAccountName: 'Checking', liabilityAccountName: 'Credit Card',
  },
];

function renderTable(onRefetch = vi.fn()) {
  render(
    <MemoryRouter>
      <MovementsTable items={items} onRefetch={onRefetch} />
    </MemoryRouter>,
  );
}

describe('MovementsTable', () => {
  it('renders all rows with badges', () => {
    renderTable();
    expect(screen.getByText('Transaction')).toBeInTheDocument();
    expect(screen.getByText('Transfer')).toBeInTheDocument();
    expect(screen.getByText('Debt Payment')).toBeInTheDocument();
    expect(screen.getByText('Rent')).toBeInTheDocument();
    expect(screen.getByText('Move savings')).toBeInTheDocument();
  });

  it('renders transfer arrow between source and destination', () => {
    renderTable();
    expect(screen.getByText(/Checking.*→.*Savings/)).toBeInTheDocument();
  });

  it('renders liability payment arrow between asset and liability', () => {
    renderTable();
    expect(screen.getByText(/Checking.*→.*Credit Card/)).toBeInTheDocument();
  });

  it('renders a row-actions trigger button for each row', () => {
    renderTable();
    const triggers = screen.getAllByRole('button', { name: /row actions/i });
    expect(triggers).toHaveLength(items.length);
  });
});
