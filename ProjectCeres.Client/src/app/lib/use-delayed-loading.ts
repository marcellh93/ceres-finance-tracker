import { useEffect, useState } from 'react';

const DEFAULT_DELAY_MS = 150;

/**
 * Suppresses a "loading" indicator for a short window so fast responses
 * never flash a skeleton. Returns true only if `loading` has been true
 * continuously for `delay` ms (default 150).
 */
export function useDelayedLoading(
  loading: boolean,
  options?: { delay?: number },
): boolean {
  const delay = options?.delay ?? DEFAULT_DELAY_MS;
  const [showSkeleton, setShowSkeleton] = useState(false);

  useEffect(() => {
    if (!loading) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- Why: immediately resetting skeleton state on loading=false is intentional; avoids an extra render frame where a stale skeleton flickers before the timeout clears it.
      setShowSkeleton(false);
      return;
    }
    const timer = setTimeout(() => setShowSkeleton(true), delay);
    return () => clearTimeout(timer);
  }, [loading, delay]);

  return showSkeleton;
}
