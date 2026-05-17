import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MovementsFilterBar, buildTypeFilterParams } from './MovementsFilterBar';

let mockFetch: ReturnType<typeof vi.fn>;

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockResolvedValue({
    ok: true,
    json: async () => [
      { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
    ],
  });
});

afterEach(() => { vi.resetAllMocks(); });

function LocationSpy({ onChange }: { onChange: (search: string) => void }) {
  const loc = useLocation();
  onChange(loc.search);
  return null;
}

function renderBar(initial = '/movements') {
  let captured = '';
  render(
    <MemoryRouter initialEntries={[initial]}>
      <Routes>
        <Route
          path="/movements"
          element={
            <>
              <MovementsFilterBar />
              <LocationSpy onChange={(s) => { captured = s; }} />
            </>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
  return () => captured;
}

describe('MovementsFilterBar', () => {
  it('renders the search input and date range picker', () => {
    renderBar();
    expect(screen.getByPlaceholderText(/search description or category/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /date range/i })).toBeInTheDocument();
  });

  it('clears all params when "Clear" is clicked', async () => {
    const getSearch = renderBar('/movements?q=hi&from=2026-04-01');
    fireEvent.click(screen.getByRole('button', { name: /clear/i }));
    await waitFor(() => {
      expect(getSearch()).toBe('');
    });
  });
});

describe('MovementsFilterBar — type filter', () => {
  it('shows "All" text in trigger when no type param is set', () => {
    renderBar('/movements');
    expect(screen.getByText('All')).toBeInTheDocument();
  });

  it('shows "Transactions" in trigger when ?type=transaction', () => {
    renderBar('/movements?type=transaction');
    expect(screen.getByText('Transactions')).toBeInTheDocument();
  });

  it('shows "Transfers" in trigger when ?type=transfer', () => {
    renderBar('/movements?type=transfer');
    expect(screen.getByText('Transfers')).toBeInTheDocument();
  });

  it('shows "Debt Payments" in trigger when ?type=liabilitypayment', () => {
    renderBar('/movements?type=liabilitypayment');
    expect(screen.getByText('Debt Payments')).toBeInTheDocument();
  });

  it('renders the Clear button when only ?type is set', async () => {
    renderBar('/movements?type=transaction');
    expect(screen.getByRole('button', { name: /clear/i })).toBeInTheDocument();
  });
});

describe('buildTypeFilterParams', () => {
  it('sets type param and removes page', () => {
    const prev = new URLSearchParams('page=2&q=hello');
    const result = buildTypeFilterParams(prev, 'transaction');
    expect(result.get('type')).toBe('transaction');
    expect(result.has('page')).toBe(false);
    expect(result.get('q')).toBe('hello');
  });

  it('removes type param when value is null (All selected)', () => {
    const prev = new URLSearchParams('type=transfer&page=3');
    const result = buildTypeFilterParams(prev, null);
    expect(result.has('type')).toBe(false);
    expect(result.has('page')).toBe(false);
  });

  it('does not mutate the original URLSearchParams', () => {
    const prev = new URLSearchParams('type=transfer');
    buildTypeFilterParams(prev, null);
    expect(prev.get('type')).toBe('transfer');
  });
});
