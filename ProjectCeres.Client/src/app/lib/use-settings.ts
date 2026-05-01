import { useEffect, useState } from 'react';

export type AppSettings = {
  numberFormat: 'comma_decimal' | 'period_decimal';
  dateFormat: string;
  defaultCurrencyCode: string;
  defaultCurrencySymbol: string;
  budgetPeriodStartDay: number;
};

export type UseSettingsResult = {
  data: AppSettings | undefined;
  loading: boolean;
};

const FALLBACK: AppSettings = {
  numberFormat: 'period_decimal',
  dateFormat: 'DD/MM/YYYY',
  defaultCurrencyCode: '',
  defaultCurrencySymbol: '',
  budgetPeriodStartDay: 1,
};

/**
 * Module-level singleton state. Settings are fetched once per session and
 * shared across every component that calls useSettings(). Subscribers are
 * notified when the fetch resolves so all consumers re-render in sync.
 *
 * Tests can call __resetSettingsForTests() to clear this state between runs.
 */
type Cache = {
  data: AppSettings | undefined;
  loading: boolean;
  promise: Promise<void> | null;
  subscribers: Set<() => void>;
};

let cache: Cache = {
  data: undefined,
  loading: false,
  promise: null,
  subscribers: new Set(),
};

function notify() {
  for (const s of cache.subscribers) s();
}

function startFetch(): Promise<void> {
  if (cache.promise) return cache.promise;
  cache.loading = true;
  notify();

  cache.promise = fetch('/api/settings')
    .then(async (response) => {
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const data = (await response.json()) as AppSettings;
      cache.data = data;
      cache.loading = false;
      notify();
    })
    .catch((error: unknown) => {
      // Fall back to a sensible default rather than blocking the UI. The
      // user's actual settings will load on next page navigation if the
      // server recovers; for now they get the conservative default.
      // eslint-disable-next-line no-console
      console.warn('[useSettings] failed to load /api/settings, using fallback:', error);
      cache.data = FALLBACK;
      cache.loading = false;
      notify();
    });

  return cache.promise;
}

export function useSettings(): UseSettingsResult {
  const [, setVersion] = useState(0);

  useEffect(() => {
    const sub = () => setVersion((v) => v + 1);
    cache.subscribers.add(sub);
    if (cache.data === undefined && !cache.loading) {
      void startFetch();
    }
    return () => {
      cache.subscribers.delete(sub);
    };
  }, []);

  return { data: cache.data, loading: cache.loading };
}

/** Test-only helper. Clears the singleton cache between test cases. */
export function __resetSettingsForTests(): void {
  cache = {
    data: undefined,
    loading: false,
    promise: null,
    subscribers: new Set(),
  };
}
