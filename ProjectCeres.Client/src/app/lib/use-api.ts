import { useCallback, useEffect, useRef, useState } from 'react';
import { COOKIE_ROTATED_HEADER, notifyUnauthenticatedIfApplicable } from './api-client';

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
    // eslint-disable-next-line react-hooks/set-state-in-effect -- Why: fetch-on-URL-change pattern; synchronously setting loading=true before the async work is the canonical SWR pattern, not a cascading-render risk.
    setState((prev) => ({ data: prev.data, error: undefined, loading: true }));

    const sendOnce = () => fetch(url, { signal: controller.signal });

    (async () => {
      let response = await sendOnce();

      // Remember-Me rotation handshake (mirrors apiFetch): when the server
      // returns 401 with the rotation header, fresh cookies are now in the
      // browser. Transparent retry — the second attempt carries the new
      // __Host-Session and authenticates. Without this, dashboard tile data
      // would render as error state after rotation even though the user
      // is signed in for the next request.
      if (
        response.status === 401 &&
        response.headers.get(COOKIE_ROTATED_HEADER) === 'true'
      ) {
        response = await sendOnce();
      }

      if (!response.ok) {
        // Genuine 401 — let the auth-context know so RequireAuth can redirect.
        if (response.status === 401) {
          notifyUnauthenticatedIfApplicable(url, response);
        }
        throw new Error(`HTTP ${response.status}`);
      }
      const data = (await response.json()) as T;
      if (!mountedRef.current) return;
      setState({ data, error: undefined, loading: false });
    })()
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
