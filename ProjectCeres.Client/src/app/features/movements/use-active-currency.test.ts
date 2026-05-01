import { renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode } from 'react';
import { createElement } from 'react';
import { __resetSettingsForTests } from '../../lib/use-settings';
import {
  LAST_CURRENCY_STORAGE_KEY,
  useActiveCurrency,
} from './use-active-currency';

const mockFetch = vi.fn();

function wrapperFactory(initialEntries: string[]) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return createElement(MemoryRouter, { initialEntries }, children);
  };
}

const ACCOUNTS_EUR_USD = [
  { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
  { id: 'a2', name: 'Savings USD', currencyCode: 'USD', currencySymbol: '$', accountTypeName: 'Asset' },
];

function settingsResponse(defaultCurrencyCode: string) {
  return {
    numberFormat: 'period_decimal',
    dateFormat: 'DD/MM/YYYY',
    defaultCurrencyCode,
    defaultCurrencySymbol: '$',
  };
}

beforeEach(() => {
  __resetSettingsForTests();
  window.localStorage.clear();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url.startsWith('/api/accounts/active')) {
      return Promise.resolve({ ok: true, json: async () => ACCOUNTS_EUR_USD });
    }
    if (url.startsWith('/api/settings')) {
      return Promise.resolve({ ok: true, json: async () => settingsResponse('EUR') });
    }
    return Promise.reject(new Error(`unexpected fetch: ${url}`));
  });
});

afterEach(() => {
  vi.resetAllMocks();
  window.localStorage.clear();
});

describe('useActiveCurrency', () => {
  it('URL ?currency wins when present and valid', async () => {
    window.localStorage.setItem(LAST_CURRENCY_STORAGE_KEY, 'EUR');
    const { result } = renderHook(() => useActiveCurrency(), {
      wrapper: wrapperFactory(['/movements?currency=USD']),
    });
    await waitFor(() => {
      expect(result.current.activeCurrency).toBe('USD');
    });
    expect(result.current.availableCurrencies).toEqual(['EUR', 'USD']);
  });

  it('localStorage wins over Settings when URL is empty and value is valid', async () => {
    window.localStorage.setItem(LAST_CURRENCY_STORAGE_KEY, 'USD');
    const { result } = renderHook(() => useActiveCurrency(), {
      wrapper: wrapperFactory(['/movements']),
    });
    await waitFor(() => {
      expect(result.current.activeCurrency).toBe('USD');
    });
  });

  it('Settings.defaultCurrencyCode wins when URL and localStorage are empty', async () => {
    const { result } = renderHook(() => useActiveCurrency(), {
      wrapper: wrapperFactory(['/movements']),
    });
    await waitFor(() => {
      expect(result.current.activeCurrency).toBe('EUR');
    });
    // localStorage should now have been written.
    expect(window.localStorage.getItem(LAST_CURRENCY_STORAGE_KEY)).toBe('EUR');
  });

  it('falls back to first sorted currency in accounts when no other signal applies', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url.startsWith('/api/accounts/active')) {
        return Promise.resolve({ ok: true, json: async () => ACCOUNTS_EUR_USD });
      }
      if (url.startsWith('/api/settings')) {
        // Default currency the user owns NO accounts in.
        return Promise.resolve({ ok: true, json: async () => settingsResponse('GBP') });
      }
      return Promise.reject(new Error(`unexpected fetch: ${url}`));
    });

    const { result } = renderHook(() => useActiveCurrency(), {
      wrapper: wrapperFactory(['/movements']),
    });
    await waitFor(() => {
      expect(result.current.activeCurrency).toBe('EUR');
    });
  });

  it('returns activeCurrency=null when the user has no accounts', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url.startsWith('/api/accounts/active')) {
        return Promise.resolve({ ok: true, json: async () => [] });
      }
      if (url.startsWith('/api/settings')) {
        return Promise.resolve({ ok: true, json: async () => settingsResponse('EUR') });
      }
      return Promise.reject(new Error(`unexpected fetch: ${url}`));
    });

    const { result } = renderHook(() => useActiveCurrency(), {
      wrapper: wrapperFactory(['/movements']),
    });
    await waitFor(() => {
      expect(result.current.loading).toBe(false);
    });
    expect(result.current.activeCurrency).toBeNull();
    expect(result.current.availableCurrencies).toEqual([]);
  });
});
