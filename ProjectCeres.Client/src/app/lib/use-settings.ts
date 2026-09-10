import { useEffect, useState } from 'react';

export type AppSettings = {
  numberFormat: 'comma_decimal' | 'period_decimal';
  dateFormat: string;
  defaultCurrencyCode: string;
  defaultCurrencySymbol: string;
  periodStartDay: number;
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
  periodStartDay: 1,
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

/**
 * Force a refresh of the cached settings. Notifies every useSettings()
 * subscriber when the new data arrives. Call this after PATCH /api/settings
 * succeeds so the rest of the app picks up format/currency changes
 * without a page reload.
 *
 * Does NOT clear cache.data first — old data stays visible for the ~50–100 ms
 * the GET takes, avoiding a flash of empty state in every other component.
 */
export function refetchSettings(): Promise<void> {
  cache.promise = null;     // clear the dedup so startFetch actually runs
  cache.loading = false;
  return startFetch();
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

/**
 * Test-only helper. Seeds the singleton with resolved data (FALLBACK by default)
 * so a component that calls useSettings() renders with valid settings
 * synchronously — no fetch, no cross-test leak. The global test setup calls this
 * in beforeEach; a test that needs the un-seeded fetch lifecycle (use-settings's
 * own tests) calls __resetSettingsForTests() in its later-running local beforeEach.
 */
export function __seedSettingsForTests(data: AppSettings = FALLBACK): void {
  cache = {
    data,
    loading: false,
    promise: Promise.resolve(),
    subscribers: new Set(),
  };
}
