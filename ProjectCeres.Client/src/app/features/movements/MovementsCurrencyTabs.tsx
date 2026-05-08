import { useSearchParams } from 'react-router-dom';
import { useApi } from '../../lib/use-api';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { ACCOUNTS_ACTIVE_URL, type AccountOptionDto } from './movements-api';
import { writeLastCurrency } from './use-active-currency';

type Props = {
  availableCurrencies: string[];
  activeCurrency: string | null;
  /** True while accounts are still loading — render a placeholder of the
   *  same height so single- vs multi-currency uncertainty doesn't push the
   *  rest of the page down on data arrival. */
  loading?: boolean;
};

export function MovementsCurrencyTabs({ availableCurrencies, activeCurrency, loading }: Props) {
  const [params, setParams] = useSearchParams();
  // We need accounts again to know which currency the currently-selected
  // account belongs to, so we can clear ?accountId when it doesn't fit the
  // newly-selected currency. Reusing the cached useApi response from the hook
  // would be nicer, but the SPA hasn't adopted TanStack Query yet — useApi
  // re-fetches per URL key, and ACCOUNTS_ACTIVE_URL is already in flight
  // from MovementsFilterBar/use-active-currency, so React's request
  // de-duplication via the browser cache is fine here.
  const { data: accounts } = useApi<AccountOptionDto[]>(ACCOUNTS_ACTIVE_URL);

  // Three states:
  //   loading           → placeholder (we don't know yet whether tabs will show)
  //   resolved single   → null (single-currency users get no dead space)
  //   resolved multi    → tabs
  if (loading) return <Skeleton className="h-8 w-32" />;
  if (availableCurrencies.length < 2) return null;

  function handleChange(next: string) {
    if (!next || next === activeCurrency) return;
    writeLastCurrency(next);

    const params2 = new URLSearchParams(params);
    params2.set('currency', next);

    // Clear ?accountId if its account belongs to a different currency.
    const accountId = params.get('accountId');
    if (accountId && accounts) {
      const acc = accounts.find((a) => a.id === accountId);
      if (acc && acc.currencyCode !== next) {
        params2.delete('accountId');
      }
    }
    // Reset pagination on filter switch.
    params2.delete('page');

    setParams(params2); // push (default) so back-button restores the previous currency
  }

  return (
    <Tabs
      value={activeCurrency ?? availableCurrencies[0]}
      onValueChange={(v) => handleChange(String(v))}
    >
      <TabsList className="w-full md:w-fit">
        {availableCurrencies.map((code) => (
          <TabsTrigger key={code} value={code} className="flex-1 md:flex-none">
            {code}
          </TabsTrigger>
        ))}
      </TabsList>
    </Tabs>
  );
}
