import { Badge } from '@/components/ui/badge';
import { Numeric } from '@/components/Numeric';
import { MovementClearedToggle } from './MovementClearedToggle';
import { MovementRowMenu } from './MovementRowMenu';
import type { MovementListItemDto } from './movements-api';
import { MOVEMENT_TYPE_LABEL, amountColor } from './movement-type-display';
import { formatNumberForDisplay } from '../../lib/amount-format';
import { formatDate } from '../../lib/date-format';
import { useSettings } from '../../lib/use-settings';

type Props = { items: MovementListItemDto[]; onRefetch: () => void };

export function MovementsTable({ items, onRefetch }: Props) {
  const settings = useSettings();
  // Fall back to period_decimal until settings load — same default the
  // useSettings error path uses.
  const numberFormat = settings.data?.numberFormat ?? 'period_decimal';
  const dateFormat = settings.data?.dateFormat;

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
            <th className="px-3 py-2 w-12"></th>
          </tr>
        </thead>
        <tbody>
          {items.map((item) => (
            <tr
              key={`${item.movementType}-${item.id}`}
              className="border-t border-border"
              style={{ viewTransitionName: `movement-row-${item.id}` }}
            >
              <td className="px-3 py-2 whitespace-nowrap">{formatDate(item.date, dateFormat)}</td>
              <td className="px-3 py-2">
                {item.movementType === 'Transaction' && (
                  <Badge variant="info">{MOVEMENT_TYPE_LABEL.Transaction}</Badge>
                )}
                {item.movementType === 'Transfer' && (
                  <Badge className="bg-chart-6/10 text-chart-6">{MOVEMENT_TYPE_LABEL.Transfer}</Badge>
                )}
                {item.movementType === 'LiabilityPayment' && (
                  <Badge className="bg-chart-7/10 text-chart-7">{MOVEMENT_TYPE_LABEL.LiabilityPayment}</Badge>
                )}
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
                  {item.currencySymbol} {formatNumberForDisplay(item.amount, numberFormat)}
                </Numeric>
              </td>
              <td className="px-3 py-2">
                <MovementClearedToggle id={item.id} type={item.movementType} isCleared={item.isCleared} />
              </td>
              <td className="px-3 py-2 text-right">
                <MovementRowMenu
                  movementId={item.id}
                  movementType={item.movementType}
                  isOpeningBalance={item.isOpeningBalance}
                  onDeleted={onRefetch}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
