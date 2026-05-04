import { renderHook, act } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { usePagination } from './usePagination';

const items = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13];

describe('usePagination', () => {
  it('returns first page of items', () => {
    const { result } = renderHook(() => usePagination(items, 6));
    expect(result.current.paginatedItems).toEqual([1, 2, 3, 4, 5, 6]);
    expect(result.current.currentPage).toBe(1);
    expect(result.current.totalPages).toBe(3);
  });

  it('next() advances page', () => {
    const { result } = renderHook(() => usePagination(items, 6));
    act(() => result.current.next());
    expect(result.current.paginatedItems).toEqual([7, 8, 9, 10, 11, 12]);
    expect(result.current.currentPage).toBe(2);
  });

  it('prev() goes back', () => {
    const { result } = renderHook(() => usePagination(items, 6));
    act(() => result.current.next());
    act(() => result.current.prev());
    expect(result.current.currentPage).toBe(1);
  });

  it('next() clamps at last page', () => {
    const { result } = renderHook(() => usePagination(items, 6));
    act(() => result.current.next());
    act(() => result.current.next());
    act(() => result.current.next()); // already on last page
    expect(result.current.currentPage).toBe(3);
  });

  it('prev() clamps at first page', () => {
    const { result } = renderHook(() => usePagination(items, 6));
    act(() => result.current.prev());
    expect(result.current.currentPage).toBe(1);
  });

  it('last page returns remaining items', () => {
    const { result } = renderHook(() => usePagination(items, 6));
    act(() => result.current.next());
    act(() => result.current.next());
    expect(result.current.paginatedItems).toEqual([13]);
  });

  it('goTo() jumps to specific page', () => {
    const { result } = renderHook(() => usePagination(items, 6));
    act(() => result.current.goTo(2));
    expect(result.current.currentPage).toBe(2);
  });

  it('empty items returns page 1 of 1 with empty array', () => {
    const { result } = renderHook(() => usePagination([], 6));
    expect(result.current.paginatedItems).toEqual([]);
    expect(result.current.totalPages).toBe(1);
  });
});
