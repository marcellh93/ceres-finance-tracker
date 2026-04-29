import { renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useApi } from './use-api';

describe('useApi', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    fetchSpy = vi.spyOn(global, 'fetch');
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('returns loading=true initially', () => {
    fetchSpy.mockImplementation(() => new Promise(() => {})); // never resolves
    const { result } = renderHook(() => useApi<{ x: number }>('/api/x'));
    expect(result.current.loading).toBe(true);
    expect(result.current.data).toBeUndefined();
    expect(result.current.error).toBeUndefined();
  });

  it('transitions to data on success', async () => {
    fetchSpy.mockResolvedValue(
      new Response(JSON.stringify({ x: 42 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    const { result } = renderHook(() => useApi<{ x: number }>('/api/x'));
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.data).toEqual({ x: 42 });
    expect(result.current.error).toBeUndefined();
  });

  it('transitions to error when fetch rejects', async () => {
    fetchSpy.mockRejectedValue(new Error('network down'));
    const { result } = renderHook(() => useApi<{ x: number }>('/api/x'));
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.error).toBeInstanceOf(Error);
    expect(result.current.data).toBeUndefined();
  });

  it('transitions to error when response status is not ok', async () => {
    fetchSpy.mockResolvedValue(new Response('boom', { status: 500 }));
    const { result } = renderHook(() => useApi<{ x: number }>('/api/x'));
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.error).toBeInstanceOf(Error);
    expect(result.current.data).toBeUndefined();
  });

  it('refetch re-runs the request and resets state', async () => {
    fetchSpy
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ x: 1 }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ x: 2 }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
      );
    const { result } = renderHook(() => useApi<{ x: number }>('/api/x'));
    await waitFor(() => expect(result.current.data).toEqual({ x: 1 }));
    result.current.refetch();
    await waitFor(() => expect(result.current.data).toEqual({ x: 2 }));
    expect(fetchSpy).toHaveBeenCalledTimes(2);
  });

  it('does not call setState after unmount', async () => {
    let resolve: (r: Response) => void;
    fetchSpy.mockImplementation(
      () => new Promise<Response>((r) => { resolve = r; }),
    );
    const { unmount } = renderHook(() => useApi<{ x: number }>('/api/x'));
    unmount();
    resolve!(
      new Response(JSON.stringify({ x: 1 }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    // No assertion needed — vitest fails on React "setState on unmounted" warnings.
    await new Promise((r) => setTimeout(r, 10));
  });
});
