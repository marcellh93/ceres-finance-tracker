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
      setShowSkeleton(false);
      return;
    }
    const timer = setTimeout(() => setShowSkeleton(true), delay);
    return () => clearTimeout(timer);
  }, [loading, delay]);

  return showSkeleton;
}
