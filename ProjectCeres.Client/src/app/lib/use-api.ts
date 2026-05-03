import { useCallback, useEffect, useRef, useState } from 'react';

type State<T> = {
  data: T | undefined;
  error: Error | undefined;
  loading: boolean;
};

export type UseApiResult<T> = State<T> & {
  /** Re-run the request. Resets state to loading. */
  refetch: () => void;
};

/**
 * Fetch a JSON endpoint and track loading/data/error state.
 * Cancels the in-flight request on unmount.
 *
 * Standard data-fetching pattern for the SPA until TanStack Query
 * is adopted (see docs/planning-future.md).
 */
export function useApi<T>(url: string): UseApiResult<T> {
  const [state, setState] = useState<State<T>>({
    data: undefined,
    error: undefined,
    loading: true,
  });
  const [tick, setTick] = useState(0);
  const mountedRef = useRef(true);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    setState((prev) => ({ data: prev.data, error: undefined, loading: true }));

    fetch(url, { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) {
          throw new Error(`HTTP ${response.status}`);
        }
        const data = (await response.json()) as T;
        if (!mountedRef.current) return;
        setState({ data, error: undefined, loading: false });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted) return;
        if (!mountedRef.current) return;
        const err = error instanceof Error ? error : new Error(String(error));
        setState({ data: undefined, error: err, loading: false });
      });

    return () => controller.abort();
  }, [url, tick]);

  const refetch = useCallback(() => setTick((t) => t + 1), []);
  return { ...state, refetch };
}
