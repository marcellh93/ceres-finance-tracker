import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { __resetSettingsForTests, refetchSettings, useSettings } from './use-settings';

let mockFetch: ReturnType<typeof vi.fn>;

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  __resetSettingsForTests();
});

afterEach(() => {
  vi.resetAllMocks();
});

describe('useSettings', () => {
  it('starts loading=true on first mount, then resolves with data', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        numberFormat: 'comma_decimal',
        dateFormat: 'DD/MM/YYYY',
        defaultCurrencyCode: 'EUR',
        defaultCurrencySymbol: '€',
      }),
    });

    const { result } = renderHook(() => useSettings());

    expect(result.current.loading).toBe(true);
    expect(result.current.data).toBeUndefined();

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
    });

    expect(result.current.data).toEqual({
      numberFormat: 'comma_decimal',
      dateFormat: 'DD/MM/YYYY',
      defaultCurrencyCode: 'EUR',
      defaultCurrencySymbol: '€',
    });
  });

  it('falls back to period_decimal defaults when the request errors', async () => {
    mockFetch.mockResolvedValue({
      ok: false,
      status: 500,
      json: async () => ({}),
    });

    const { result } = renderHook(() => useSettings());

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
    });

    expect(result.current.data?.numberFormat).toBe('period_decimal');
  });

  it('only fetches once across multiple hook consumers', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        numberFormat: 'period_decimal',
        dateFormat: 'MM/DD/YYYY',
        defaultCurrencyCode: 'USD',
        defaultCurrencySymbol: '$',
      }),
    });

    const a = renderHook(() => useSettings());
    const b = renderHook(() => useSettings());

    await waitFor(() => {
      expect(a.result.current.loading).toBe(false);
      expect(b.result.current.loading).toBe(false);
    });

    expect(mockFetch).toHaveBeenCalledTimes(1);
    expect(a.result.current.data).toEqual(b.result.current.data);
  });

  it('keeps existing subscribers in sync when data resolves later', async () => {
    let resolveFetch: (value: unknown) => void = () => {};
    const pending = new Promise((resolve) => {
      resolveFetch = resolve;
    });
    mockFetch.mockReturnValue(pending);

    const { result } = renderHook(() => useSettings());
    expect(result.current.loading).toBe(true);

    await act(async () => {
      resolveFetch({
        ok: true,
        json: async () => ({
          numberFormat: 'comma_decimal',
          dateFormat: 'DD/MM/YYYY',
          defaultCurrencyCode: 'EUR',
          defaultCurrencySymbol: '€',
        }),
      });
    });

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
    });
    expect(result.current.data?.numberFormat).toBe('comma_decimal');
  });
});

describe('refetchSettings', () => {
  it('triggers a new fetch and notifies subscribers', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        numberFormat: 'comma_decimal',
        dateFormat: 'DD/MM/YYYY',
        defaultCurrencyCode: 'EUR',
        defaultCurrencySymbol: '€',
        periodStartDay: 1,
      }),
    });

    const { result } = renderHook(() => useSettings());
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(mockFetch).toHaveBeenCalledTimes(1);

    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        numberFormat: 'period_decimal',
        dateFormat: 'YYYY-MM-DD',
        defaultCurrencyCode: 'USD',
        defaultCurrencySymbol: '$',
        periodStartDay: 15,
      }),
    });

    await act(async () => {
      await refetchSettings();
    });

    expect(mockFetch).toHaveBeenCalledTimes(2);
  });

  it('updates cache.data so existing subscribers see the new response', async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        numberFormat: 'comma_decimal',
        dateFormat: 'DD/MM/YYYY',
        defaultCurrencyCode: 'EUR',
        defaultCurrencySymbol: '€',
        periodStartDay: 1,
      }),
    });

    const { result } = renderHook(() => useSettings());
    await waitFor(() => expect(result.current.data?.defaultCurrencyCode).toBe('EUR'));

    mockFetch.mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        numberFormat: 'period_decimal',
        dateFormat: 'YYYY-MM-DD',
        defaultCurrencyCode: 'USD',
        defaultCurrencySymbol: '$',
        periodStartDay: 15,
      }),
    });

    await act(async () => {
      await refetchSettings();
    });

    await waitFor(() =>
      expect(result.current.data?.defaultCurrencyCode).toBe('USD'),
    );
    expect(result.current.data?.numberFormat).toBe('period_decimal');
    expect(result.current.data?.periodStartDay).toBe(15);
  });
});
