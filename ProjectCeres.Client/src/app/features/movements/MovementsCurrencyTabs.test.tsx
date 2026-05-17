import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MovementsCurrencyTabs } from './MovementsCurrencyTabs';
import { LAST_CURRENCY_STORAGE_KEY } from './use-active-currency';

let mockFetch: ReturnType<typeof vi.fn>;

const ACCOUNTS = [
  { id: 'a1', name: 'EUR Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
  { id: 'a2', name: 'USD Savings', currencyCode: 'USD', currencySymbol: '$', accountTypeName: 'Asset' },
];

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockResolvedValue({ ok: true, json: async () => ACCOUNTS });
  window.localStorage.clear();
});

afterEach(() => {
  vi.resetAllMocks();
  window.localStorage.clear();
});

function LocationSpy({ onChange }: { onChange: (search: string) => void }) {
  const loc = useLocation();
  onChange(loc.search);
  return null;
}

function renderTabs(opts: {
  initial: string;
  availableCurrencies: string[];
  activeCurrency: string | null;
}) {
  let captured = '';
  render(
    <MemoryRouter initialEntries={[opts.initial]}>
      <Routes>
        <Route
          path="/movements"
          element={
            <>
              <MovementsCurrencyTabs
                availableCurrencies={opts.availableCurrencies}
                activeCurrency={opts.activeCurrency}
              />
              <LocationSpy onChange={(s) => { captured = s; }} />
            </>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
  return () => captured;
}

describe('MovementsCurrencyTabs', () => {
  it('renders nothing when fewer than 2 currencies are available', () => {
    renderTabs({ initial: '/movements?currency=EUR', availableCurrencies: ['EUR'], activeCurrency: 'EUR' });
    expect(screen.queryByRole('tab')).toBeNull();
  });

  it('renders one tab per ISO currency code when 2+ are available', () => {
    renderTabs({
      initial: '/movements?currency=EUR',
      availableCurrencies: ['EUR', 'USD'],
      activeCurrency: 'EUR',
    });
    expect(screen.getByRole('tab', { name: 'EUR' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'USD' })).toBeInTheDocument();
  });

  it('clicking a tab updates ?currency= and writes localStorage', async () => {
    const getSearch = renderTabs({
      initial: '/movements?currency=EUR',
      availableCurrencies: ['EUR', 'USD'],
      activeCurrency: 'EUR',
    });
    fireEvent.click(screen.getByRole('tab', { name: 'USD' }));
    await waitFor(() => {
      expect(getSearch()).toContain('currency=USD');
    });
    expect(window.localStorage.getItem(LAST_CURRENCY_STORAGE_KEY)).toBe('USD');
  });

  it('clears ?accountId when the selected account belongs to a different currency', async () => {
    const getSearch = renderTabs({
      initial: '/movements?currency=EUR&accountId=a1',
      availableCurrencies: ['EUR', 'USD'],
      activeCurrency: 'EUR',
    });
    // Wait for accounts fetch to resolve so the component knows a1 is EUR.
    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalled();
    });
    fireEvent.click(screen.getByRole('tab', { name: 'USD' }));
    await waitFor(() => {
      expect(getSearch()).toContain('currency=USD');
    });
    expect(getSearch()).not.toContain('accountId=');
  });
});
