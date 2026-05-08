import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { MovementsCardList } from './MovementsCardList';
import type { MovementListItemDto } from './movements-api';

vi.mock('../../lib/use-settings', () => ({
  useSettings: () => ({
    data: { numberFormat: 'period_decimal', dateFormat: 'YYYY-MM-DD' },
    error: undefined,
    loading: false,
    refetch: vi.fn(),
  }),
}));

beforeEach(() => {
  globalThis.fetch = vi.fn(() => Promise.resolve(new Response(null, { status: 204 }))) as unknown as typeof fetch;
});

const transaction: MovementListItemDto = {
  id: 't1',
  movementType: 'Transaction',
  date: '2026-03-12',
  amount: 42.5,
  currencyCode: 'EUR',
  currencySymbol: '€',
  description: 'Spotify subscription',
  isCleared: true,
  isOpeningBalance: false,
  accountName: 'Checking Account (BBVA)',
  categoryName: 'Subscriptions',
  categoryTypeName: 'Expense',
  sourceAccountName: null,
  destAccountName: null,
  assetAccountName: null,
  liabilityAccountName: null,
};

const transfer: MovementListItemDto = {
  id: 'tr1',
  movementType: 'Transfer',
  date: '2026-03-13',
  amount: 100,
  currencyCode: 'EUR',
  currencySymbol: '€',
  description: null,
  isCleared: false,
  isOpeningBalance: false,
  accountName: null,
  categoryName: null,
  categoryTypeName: null,
  sourceAccountName: 'Cash',
  destAccountName: 'Savings',
  assetAccountName: null,
  liabilityAccountName: null,
};

const liabilityPayment: MovementListItemDto = {
  id: 'lp1',
  movementType: 'LiabilityPayment',
  date: '2026-03-14',
  amount: 250,
  currencyCode: 'EUR',
  currencySymbol: '€',
  description: null,
  isCleared: false,
  isOpeningBalance: false,
  accountName: null,
  categoryName: null,
  categoryTypeName: null,
  sourceAccountName: null,
  destAccountName: null,
  assetAccountName: 'Checking Account (BBVA)',
  liabilityAccountName: 'Credit Card (BBVA)',
};

const transactionNoDescription: MovementListItemDto = {
  ...transaction,
  id: 't2',
  description: null,
};

function renderList(items: MovementListItemDto[], onRefetch = vi.fn()) {
  return render(
    <MemoryRouter>
      <MovementsCardList items={items} onRefetch={onRefetch} />
    </MemoryRouter>,
  );
}

describe('MovementsCardList', () => {
  it('renders one article per item, preserving order', () => {
    renderList([transaction, transfer, liabilityPayment]);
    const articles = screen.getAllByRole('article');
    expect(articles).toHaveLength(3);
    expect(within(articles[0]).getByText('Spotify subscription')).toBeInTheDocument();
    expect(within(articles[1]).getByText('Cash → Savings')).toBeInTheDocument();
    expect(within(articles[2]).getByText('Checking Account (BBVA) → Credit Card (BBVA)')).toBeInTheDocument();
  });

  it('shows the description as primary text on a transaction card', () => {
    renderList([transaction]);
    expect(screen.getByText('Spotify subscription')).toBeInTheDocument();
    expect(screen.getByText('Checking Account (BBVA)')).toBeInTheDocument();
  });

  it('falls back to category name when transaction description is null', () => {
    renderList([transactionNoDescription]);
    expect(screen.getByText('Subscriptions')).toBeInTheDocument();
  });

  it('shows source → destination on a transfer card', () => {
    renderList([transfer]);
    expect(screen.getByText('Cash → Savings')).toBeInTheDocument();
  });

  it('shows asset → liability on a liability payment card', () => {
    renderList([liabilityPayment]);
    expect(screen.getByText('Checking Account (BBVA) → Credit Card (BBVA)')).toBeInTheDocument();
  });

  it('renders the type pill for each variant', () => {
    renderList([transaction, transfer, liabilityPayment]);
    expect(screen.getByText('Transaction')).toBeInTheDocument();
    expect(screen.getByText('Transfer')).toBeInTheDocument();
    expect(screen.getByText('Debt Payment')).toBeInTheDocument();
  });

  it('card link points to /movements/{id}/edit', () => {
    renderList([transaction]);
    const link = screen.getByRole('link');
    expect(link).toHaveAttribute('href', '/movements/t1/edit');
  });

  it('card link aria-label describes the movement', () => {
    renderList([transaction]);
    const link = screen.getByRole('link');
    expect(link).toHaveAttribute('aria-label', expect.stringContaining('Transaction'));
    expect(link).toHaveAttribute('aria-label', expect.stringContaining('2026-03-12'));
  });

  it('clicking the status toggle does NOT navigate (stopPropagation)', () => {
    renderList([transaction]);
    const toggle = screen.getByRole('button', { name: /mark as pending/i });
    const link = screen.getByRole('link');
    const linkClickSpy = vi.fn();
    link.addEventListener('click', linkClickSpy);
    fireEvent.click(toggle);
    expect(linkClickSpy).not.toHaveBeenCalled();
  });

  it('renders pending badge for an uncleared movement', () => {
    renderList([transfer]);
    expect(screen.getByText(/pending/i)).toBeInTheDocument();
  });

  it('list container exposes the list role implicitly', () => {
    renderList([transaction, transfer]);
    const list = screen.getByRole('list');
    const items = within(list).getAllByRole('listitem');
    expect(items).toHaveLength(2);
  });
});
