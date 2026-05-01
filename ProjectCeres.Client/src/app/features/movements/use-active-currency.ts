import { useEffect, useMemo, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useApi } from '../../lib/use-api';
import { useSettings } from '../../lib/use-settings';
import { ACCOUNTS_ACTIVE_URL, type AccountOptionDto } from './movements-api';

export const LAST_CURRENCY_STORAGE_KEY = 'movements:lastCurrency';

export function readLastCurrency(): string | null {
  try {
    return window.localStorage.getItem(LAST_CURRENCY_STORAGE_KEY);
  } catch {
    return null;
  }
}

export function writeLastCurrency(code: string): void {
  try {
    window.localStorage.setItem(LAST_CURRENCY_STORAGE_KEY, code);
  } catch {
    // localStorage may be unavailable (e.g. SSR, private mode); silently ignore.
  }
}

export type UseActiveCurrencyResult = {
  availableCurrencies: string[];
  activeCurrency: string | null;
  loading: boolean;
  accounts: AccountOptionDto[];
};

/**
 * Resolves the active currency for the Movements surface.
 *
 * Priority on first resolve:
 *   1. URL ?currency=<code> if valid
 *   2. localStorage 'movements:lastCurrency' if valid
 *   3. useSettings().data.defaultCurrencyCode if valid
 *   4. First currency in the accounts list (sorted alphabetically)
 *
 * On first resolve (only when the URL lacks ?currency), the chosen code is
 * mirrored back into the URL via setSearchParams(..., { replace: true }) and
 * into localStorage.
 *
 * Returns activeCurrency=null only during the brief loading window before
 * settings/accounts resolve, OR when the user has zero accounts.
 */
export function useActiveCurrency(): UseActiveCurrencyResult {
  const { data: accounts, loading: accountsLoading } = useApi<AccountOptionDto[]>(ACCOUNTS_ACTIVE_URL);
  const { data: settings, loading: settingsLoading } = useSettings();
  const [params, setParams] = useSearchParams();
  const syncedRef = useRef(false);

  const availableCurrencies = useMemo(() => {
    if (!Array.isArray(accounts)) return [];
    const codes = new Set<string>();
    for (const a of accounts) codes.add(a.currencyCode);
    return Array.from(codes).sort();
  }, [accounts]);

  const loading = accountsLoading || settingsLoading;

  const urlCurrency = params.get('currency');

  let activeCurrency: string | null = null;
  if (!loading && accounts && availableCurrencies.length > 0) {
    if (urlCurrency && availableCurrencies.includes(urlCurrency)) {
      activeCurrency = urlCurrency;
    } else {
      const stored = readLastCurrency();
      if (stored && availableCurrencies.includes(stored)) {
        activeCurrency = stored;
      } else if (settings?.defaultCurrencyCode && availableCurrencies.includes(settings.defaultCurrencyCode)) {
        activeCurrency = settings.defaultCurrencyCode;
      } else {
        activeCurrency = availableCurrencies[0];
      }
    }
  }

  useEffect(() => {
    if (syncedRef.current) return;
    if (loading) return;
    if (!activeCurrency) return;

    // Always remember the active code; sync URL only if it isn't already there.
    writeLastCurrency(activeCurrency);

    if (params.get('currency') !== activeCurrency) {
      const next = new URLSearchParams(params);
      next.set('currency', activeCurrency);
      setParams(next, { replace: true });
    }
    syncedRef.current = true;
  }, [loading, activeCurrency, params, setParams]);

  return {
    availableCurrencies,
    activeCurrency,
    loading,
    accounts: Array.isArray(accounts) ? accounts : [],
  };
}
