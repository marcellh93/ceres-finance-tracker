import { useEffect, useState } from 'react';

/**
 * Returns `value` delayed by `delayMs`. Each new `value` resets the timer.
 * Useful for debouncing search inputs before they propagate to URL state or API calls.
 */
export function useDebounced<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState<T>(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);

  return debounced;
}
