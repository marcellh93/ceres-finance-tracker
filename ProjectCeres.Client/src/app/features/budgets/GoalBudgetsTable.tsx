import { Badge } from '@/components/ui/badge';
import { Numeric } from '@/components/Numeric';
import { BudgetProgressBar } from './BudgetProgressBar';
import { BudgetRowMenu } from './BudgetRowMenu';
import type { GoalBudgetListItemDto } from './budgets-api';
import { formatNumberForDisplay } from '../../lib/amount-format';
import { formatDate } from '../../lib/date-format';
import { useSettings } from '../../lib/use-settings';

type Props = {
  items: GoalBudgetListItemDto[];
  onChanged: () => void;
};

export function GoalBudgetsTable({ items, onChanged }: Props) {
  const settings = useSettings();
  const numberFormat = settings.data?.numberFormat ?? 'period_decimal';
  const dateFormat = settings.data?.dateFormat;

  return (
    <div className="rounded-md border border-border overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="bg-muted/40">
          <tr>
            <th className="text-left px-3 py-2 font-medium">Name</th>
            <th className="text-left px-3 py-2 font-medium">Type</th>
            <th className="text-left px-3 py-2 font-medium">Currency</th>
            <th className="text-right px-3 py-2 font-medium">Target</th>
            <th className="text-left px-3 py-2 font-medium w-64">Progress</th>
            <th className="text-left px-3 py-2 font-medium">End date</th>
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
                  <span className="font-medium">{item.name}</span>
                  {!item.isActive && <Badge variant="secondary">Archived</Badge>}
                </div>
              </td>
              <td className="px-3 py-2">
                {item.goalType === 'Spending' ? (
                  <Badge className="bg-chart-3/10 text-chart-3">Spending</Badge>
                ) : (
                  <Badge className="bg-chart-2/10 text-chart-2">Savings</Badge>
                )}
              </td>
              <td className="px-3 py-2 whitespace-nowrap">
                {item.currencySymbol} {item.currencyCode}
              </td>
              <td className="px-3 py-2 text-right whitespace-nowrap">
                <Numeric>
                  {item.currencySymbol} {formatNumberForDisplay(item.targetAmount, numberFormat)}
                </Numeric>
              </td>
              <td className="px-3 py-2">
                <BudgetProgressBar
                  progress={item.progress}
                  target={item.targetAmount}
                  currencySymbol={item.currencySymbol}
                />
              </td>
              <td className="px-3 py-2 whitespace-nowrap">
                {item.endDate ? formatDate(item.endDate, dateFormat) : '—'}
              </td>
              <td className="px-3 py-2 text-right">
                <BudgetRowMenu
                  budgetId={item.id}
                  kind="GoalBudget"
                  isActive={item.isActive}
                  noun={item.goalType === 'Spending' ? 'spending goal' : 'savings goal'}
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
