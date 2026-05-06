import { DateRangePicker } from '@/components/DateRangePicker';
import { CurrencyCombobox } from '@/components/CurrencyCombobox';
import { useReportsFilters } from './useReportsFilters';

export function ReportsSharedFilterBar() {
  const { filters, setFilter } = useReportsFilters();

  return (
    <div className="bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/60">
      <div className="grid grid-cols-1 gap-2 px-[8%] py-2 sm:grid-cols-2">
        <DateRangePicker fromKey="from" toKey="to" className="w-full" />
        <CurrencyCombobox
          value={filters.currencyId}
          onChange={(id) => setFilter('currencyId', id)}
          placeholder="Currency"
          className="w-full"
        />
      </div>
    </div>
  );
}
