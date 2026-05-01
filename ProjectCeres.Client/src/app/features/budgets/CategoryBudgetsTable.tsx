import { Badge } from '@/components/ui/badge';
import { Numeric } from '@/components/Numeric';
import { BudgetProgressBar } from './BudgetProgressBar';
import { BudgetRowMenu } from './BudgetRowMenu';
import type { CategoryBudgetListItemDto } from './budgets-api';
import { formatNumberForDisplay } from '../../lib/amount-format';
import { useSettings } from '../../lib/use-settings';

type Props = {
  items: CategoryBudgetListItemDto[];
  onChanged: () => void;
};

export function CategoryBudgetsTable({ items, onChanged }: Props) {
  const settings = useSettings();
  const numberFormat = settings.data?.numberFormat ?? 'period_decimal';

  return (
    <div className="rounded-md border border-border overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="bg-muted/40">
          <tr>
            <th className="text-left px-3 py-2 font-medium">Category</th>
            <th className="text-left px-3 py-2 font-medium">Currency</th>
            <th className="text-right px-3 py-2 font-medium">This period</th>
            <th className="text-left px-3 py-2 font-medium w-64">Progress</th>
            <th className="px-3 py-2 w-12"></th>
          </tr>
        </thead>
        <tbody>
          {items.map((item) => (
            <tr
              key={item.id}
              className={`border-t border-border ${item.isActive ? '' : 'opacity-60'}`}
            >
              <td className="px-3 py-2">
                <div className="flex items-center gap-2">
                  <span>{item.categoryName}</span>
                  {!item.isActive && <Badge variant="secondary">Archived</Badge>}
                </div>
              </td>
              <td className="px-3 py-2 whitespace-nowrap">
                {item.currencySymbol} {item.currencyCode}
              </td>
              <td className="px-3 py-2 text-right whitespace-nowrap">
                <Numeric>
                  {item.currencySymbol} {formatNumberForDisplay(item.currentPeriodSpend, numberFormat)}
                </Numeric>
                <span className="text-muted-foreground"> / </span>
                <Numeric className="text-muted-foreground">
                  {item.currencySymbol} {formatNumberForDisplay(item.limitAmount, numberFormat)}
                </Numeric>
              </td>
              <td className="px-3 py-2">
                <BudgetProgressBar
                  progress={item.currentPeriodSpend}
                  target={item.limitAmount}
                  currencySymbol=""
                />
              </td>
              <td className="px-3 py-2 text-right">
                <BudgetRowMenu
                  budgetId={item.id}
                  kind="CategoryBudget"
                  isActive={item.isActive}
                  noun="category budget"
                  onChanged={onChanged}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
