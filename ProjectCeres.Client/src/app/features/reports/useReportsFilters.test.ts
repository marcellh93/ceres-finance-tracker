import { renderHook, act } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, beforeEach, afterEach, vi } from 'vitest';
import { createElement } from 'react';
import { useReportsFilters } from './useReportsFilters';

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  vi.setSystemTime(new Date(2026, 4, 1, 12, 0, 0)); // 2026-05-01
});
afterEach(() => { vi.useRealTimers(); });

function wrapper({ children }: { children: React.ReactNode }) {
  return createElement(MemoryRouter, { initialEntries: ['/reports'] }, children);
}

describe('useReportsFilters', () => {
  it('defaults from/to to current month when absent', () => {
    const { result } = renderHook(() => useReportsFilters(), { wrapper });
    expect(result.current.filters.from).toBe('2026-05-01');
    expect(result.current.filters.to).toBe('2026-05-31');
  });

  it('setFilter updates a single key', () => {
    const { result } = renderHook(() => useReportsFilters(), { wrapper });
    act(() => result.current.setFilter('currencyId', 2));
    expect(result.current.filters.currencyId).toBe(2);
  });

  it('toQueryString encodes present filters only', () => {
    const { result } = renderHook(() => useReportsFilters(), { wrapper });
    act(() => result.current.setFilter('currencyId', 3));
    const qs = result.current.toQueryString();
    expect(qs).toContain('currencyId=3');
    expect(qs).toContain('from=2026-05-01');
    expect(qs).not.toContain('accountId');
  });

  it('reset restores defaults', () => {
    const { result } = renderHook(() => useReportsFilters(), { wrapper });
    act(() => result.current.setFilter('currencyId', 99));
    act(() => result.current.reset());
    expect(result.current.filters.currencyId).toBeNull();
  });
});
