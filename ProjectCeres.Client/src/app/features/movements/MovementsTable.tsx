import { Numeric } from '@/components/Numeric';
import { cn } from '@/lib/utils';
import { MovementClearedToggle } from './MovementClearedToggle';
import type { MovementListItemDto, MovementType } from './movements-api';

type Props = { items: MovementListItemDto[] };

const typeBadgeClass: Record<MovementType, string> = {
  Transaction: 'bg-info/10 text-info',
  Transfer: 'bg-chart-4/10 text-chart-4',
  LiabilityPayment: 'bg-warning/10 text-warning',
};

const typeLabel: Record<MovementType, string> = {
  Transaction: 'Transaction',
  Transfer: 'Transfer',
  LiabilityPayment: 'Liability Payment',
};

function formatDate(yyyyMmDd: string): string {
  const [y, m, d] = yyyyMmDd.split('-').map(Number);
  return new Date(y, m - 1, d).toLocaleDateString();
}

function amountColor(item: MovementListItemDto): string {
  if (item.movementType === 'Transaction') {
    if (item.categoryTypeName === 'Income') return 'text-success';
    if (item.categoryTypeName === 'Expense') return 'text-destructive';
  }
  return 'text-foreground';
}

export function MovementsTable({ items }: Props) {
  return (
    <div className="rounded-md border border-border overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="bg-muted/40">
          <tr>
            <th className="text-left px-3 py-2 font-medium">Date</th>
            <th className="text-left px-3 py-2 font-medium">Type</th>
            <th className="text-left px-3 py-2 font-medium">Account(s)</th>
            <th className="text-left px-3 py-2 font-medium">Category / Details</th>
            <th className="text-left px-3 py-2 font-medium">Description</th>
            <th className="text-right px-3 py-2 font-medium">Amount</th>
            <th className="text-left px-3 py-2 font-medium">Status</th>
          </tr>
        </thead>
        <tbody>
          {items.map((item) => (
            <tr key={`${item.movementType}-${item.id}`} className="border-t border-border">
              <td className="px-3 py-2 whitespace-nowrap">{formatDate(item.date)}</td>
              <td className="px-3 py-2">
                <span className={cn('inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap', typeBadgeClass[item.movementType])}>
                  {typeLabel[item.movementType]}
                </span>
              </td>
              <td className="px-3 py-2">
                {item.movementType === 'Transaction' && item.accountName}
                {item.movementType === 'Transfer' && `${item.sourceAccountName} → ${item.destAccountName}`}
                {item.movementType === 'LiabilityPayment' && `${item.assetAccountName} → ${item.liabilityAccountName}`}
              </td>
              <td className="px-3 py-2">
                {item.movementType === 'Transaction' ? item.categoryName : '—'}
              </td>
              <td className="px-3 py-2">{item.description ?? '—'}</td>
              <td className="px-3 py-2 text-right whitespace-nowrap">
                <Numeric className={amountColor(item)}>
                  {item.currencySymbol} {item.amount.toFixed(2)}
                </Numeric>
              </td>
              <td className="px-3 py-2">
                <MovementClearedToggle id={item.id} type={item.movementType} isCleared={item.isCleared} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
