import { useMatch } from 'react-router-dom';
import { DateRangePicker } from '@/components/DateRangePicker';
import { CurrencyCombobox } from '@/components/CurrencyCombobox';
import { AccountCombobox } from '../../components/AccountCombobox';
import { CategoryCombobox } from '../../components/CategoryCombobox';
import { useApi } from '../../lib/use-api';
import { ACCOUNTS_ACTIVE_URL, CATEGORIES_ACTIVE_URL, type AccountOptionDto, type CategoryOptionDto } from '../movements/movements-api';
import { useReportsFilters } from './useReportsFilters';

const USES_CURRENCY = new Set(['income-expense', 'expense-breakdown', 'transaction-history', 'budget-vs-actual', 'largest-expenses', 'monthly-cash-flow', 'net-worth-over-time']);
const USES_RANGE    = new Set(['income-expense', 'expense-breakdown', 'transaction-history', 'budget-vs-actual', 'largest-expenses', 'monthly-cash-flow', 'net-worth-over-time']);
const USES_ACCOUNT  = new Set(['transaction-history']);
const USES_CATEGORY = new Set(['expense-breakdown', 'transaction-history']);

function useCurrentSlug(): string | null {
  const match = useMatch('/reports/:slug');
  return match?.params.slug ?? null;
}

export function ReportsFilterBar() {
  const slug = useCurrentSlug();
  const { filters, setFilter } = useReportsFilters();

  const showCurrency = slug ? USES_CURRENCY.has(slug) : false;
  const showRange    = slug ? USES_RANGE.has(slug) : false;
  const showAccount  = slug ? USES_ACCOUNT.has(slug) : false;
  const showCategory = slug ? USES_CATEGORY.has(slug) : false;

  const { data: accounts } = useApi<AccountOptionDto[]>(showAccount ? ACCOUNTS_ACTIVE_URL : '');
  const { data: categories } = useApi<CategoryOptionDto[]>(showCategory ? CATEGORIES_ACTIVE_URL : '');

  if (!showRange && !showCurrency && !showAccount && !showCategory) return null;

  return (
    <div className="sticky top-14 z-10 -mx-6 border-b bg-background/95 px-6 py-3 backdrop-blur supports-[backdrop-filter]:bg-background/60">
      <div className="flex flex-wrap items-center gap-2">
        {showRange && <DateRangePicker fromKey="from" toKey="to" />}
        {showCurrency && (
          <CurrencyCombobox
            value={filters.currencyId}
            onChange={(id) => setFilter('currencyId', id)}
            placeholder="Currency"
          />
        )}
        {showAccount && (
          <AccountCombobox
            accounts={accounts ?? []}
            value={filters.accountId}
            onChange={(id) => setFilter('accountId', id)}
            placeholder="All accounts"
          />
        )}
        {showCategory && (
          <CategoryCombobox
            categories={categories ?? []}
            value={filters.categoryId}
            onChange={(id) => setFilter('categoryId', id)}
            placeholder="All categories"
          />
        )}
      </div>
    </div>
  );
}
