import { useMatch } from 'react-router-dom';
import { AccountCombobox } from '../../components/AccountCombobox';
import { CategoryCombobox } from '../../components/CategoryCombobox';
import { useApi } from '../../lib/use-api';
import { ACCOUNTS_ACTIVE_URL, CATEGORIES_ACTIVE_URL, type AccountOptionDto, type CategoryOptionDto } from '../movements/movements-api';
import { useReportsFilters } from './useReportsFilters';

const USES_ACCOUNT  = new Set(['transaction-history']);
const USES_CATEGORY = new Set(['expense-breakdown', 'transaction-history']);

function useCurrentSlug(): string | null {
  const match = useMatch('/reports/:slug');
  return match?.params.slug ?? null;
}

export function ReportLocalFilterBar() {
  const slug = useCurrentSlug();
  const { filters, setFilter } = useReportsFilters();

  const showAccount  = slug ? USES_ACCOUNT.has(slug) : false;
  const showCategory = slug ? USES_CATEGORY.has(slug) : false;

  const { data: accounts }   = useApi<AccountOptionDto[]>(showAccount ? ACCOUNTS_ACTIVE_URL : '');
  const { data: categories } = useApi<CategoryOptionDto[]>(showCategory ? CATEGORIES_ACTIVE_URL : '');

  if (!showAccount && !showCategory) return null;

  return (
    <div className="grid grid-cols-1 gap-2 px-[8%] pb-2 sm:grid-cols-2">
      {showAccount && (
        <AccountCombobox
          accounts={accounts ?? []}
          value={filters.accountId}
          onChange={(id) => setFilter('accountId', id)}
          placeholder="All accounts"
          className="w-full"
        />
      )}
      {showCategory && (
        <CategoryCombobox
          categories={categories ?? []}
          value={filters.categoryId}
          onChange={(id) => setFilter('categoryId', id)}
          placeholder="All categories"
          className="w-full"
        />
      )}
    </div>
  );
}
