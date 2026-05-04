import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useDelayedLoading } from './use-delayed-loading';

describe('useDelayedLoading', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('returns false on first render when loading is false', () => {
    const { result } = renderHook(({ loading }) => useDelayedLoading(loading), {
      initialProps: { loading: false },
    });
    expect(result.current).toBe(false);
  });

  it('returns false during the delay window after loading flips true', () => {
    const { result, rerender } = renderHook(({ loading }) => useDelayedLoading(loading), {
      initialProps: { loading: false },
    });
    rerender({ loading: true });
    expect(result.current).toBe(false);
    act(() => {
      vi.advanceTimersByTime(149);
    });
    expect(result.current).toBe(false);
  });

  it('returns true after the default 150ms delay elapses', () => {
    const { result, rerender } = renderHook(({ loading }) => useDelayedLoading(loading), {
      initialProps: { loading: false },
    });
    rerender({ loading: true });
    act(() => {
      vi.advanceTimersByTime(150);
    });
    expect(result.current).toBe(true);
  });

  it('never returns true if loading flips false within the delay window', () => {
    const { result, rerender } = renderHook(({ loading }) => useDelayedLoading(loading), {
      initialProps: { loading: false },
    });
    rerender({ loading: true });
    act(() => {
      vi.advanceTimersByTime(100);
    });
    rerender({ loading: false });
    act(() => {
      vi.advanceTimersByTime(500);
    });
    expect(result.current).toBe(false);
  });

  it('respects a custom delay option', () => {
    const { result, rerender } = renderHook(
      ({ loading }) => useDelayedLoading(loading, { delay: 50 }),
      { initialProps: { loading: false } },
    );
    rerender({ loading: true });
    act(() => {
      vi.advanceTimersByTime(49);
    });
    expect(result.current).toBe(false);
    act(() => {
      vi.advanceTimersByTime(1);
    });
    expect(result.current).toBe(true);
  });

  it('resets to false immediately when loading flips back to false', () => {
    const { result, rerender } = renderHook(({ loading }) => useDelayedLoading(loading), {
      initialProps: { loading: false },
    });
    rerender({ loading: true });
    act(() => {
      vi.advanceTimersByTime(200);
    });
    expect(result.current).toBe(true);
    rerender({ loading: false });
    expect(result.current).toBe(false);
  });

  it('does not throw when unmounted while a timer is pending', () => {
    const { rerender, unmount } = renderHook(({ loading }) => useDelayedLoading(loading), {
      initialProps: { loading: false },
    });
    rerender({ loading: true });
    unmount();
    expect(() => {
      vi.advanceTimersByTime(500);
    }).not.toThrow();
  });
});
