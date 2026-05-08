import { Link } from 'react-router-dom';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import { Numeric } from '@/components/Numeric';
import { MovementClearedToggle } from './MovementClearedToggle';
import { MovementRowMenu } from './MovementRowMenu';
import type { MovementListItemDto } from './movements-api';
import { MOVEMENT_TYPE_LABEL, amountColor } from './movement-type-display';
import { formatNumberForDisplay } from '../../lib/amount-format';
import { formatDate } from '../../lib/date-format';
import { useSettings } from '../../lib/use-settings';

type Props = { items: MovementListItemDto[]; onRefetch: () => void };

function typePill(item: MovementListItemDto) {
  if (item.movementType === 'Transaction') {
    return <Badge variant="info">{MOVEMENT_TYPE_LABEL.Transaction}</Badge>;
  }
  if (item.movementType === 'Transfer') {
    return <Badge className="bg-chart-6/10 text-chart-6">{MOVEMENT_TYPE_LABEL.Transfer}</Badge>;
  }
  return <Badge className="bg-chart-7/10 text-chart-7">{MOVEMENT_TYPE_LABEL.LiabilityPayment}</Badge>;
}

function accountLine(item: MovementListItemDto): string {
  if (item.movementType === 'Transaction') return item.accountName ?? '';
  if (item.movementType === 'Transfer') return `${item.sourceAccountName} → ${item.destAccountName}`;
  return `${item.assetAccountName} → ${item.liabilityAccountName}`;
}

function primaryLine(item: MovementListItemDto): string {
  if (item.description) return item.description;
  if (item.movementType === 'Transaction' && item.categoryName) return item.categoryName;
  // For Transfer and LiabilityPayment with no description, surface the account
  // route as the primary line — the type pill already conveys the movement type.
  return accountLine(item);
}

export function MovementsCardList({ items, onRefetch }: Props) {
  const settings = useSettings();
  const numberFormat = settings.data?.numberFormat ?? 'period_decimal';
  const dateFormat = settings.data?.dateFormat;

  return (
    <ul className="space-y-2" role="list">
      {items.map((item) => {
        const dateLabel = formatDate(item.date, dateFormat);
        const primary = primaryLine(item);
        const account = accountLine(item);
        // For Transfer / LiabilityPayment with no description the account route
        // becomes the primary line, so suppress the redundant secondary row.
        const showAccountLine = account !== primary && account !== '';
        return (
          <li key={`${item.movementType}-${item.id}`}>
            <article
              className={cn(
                'group relative rounded-lg border border-border bg-card text-card-foreground',
                'transition-colors [transition-duration:var(--motion-duration-base)]',
                'hover:bg-accent/40 focus-within:ring-2 focus-within:ring-ring',
              )}
              style={{ viewTransitionName: `movement-row-${item.id}` }}
            >
              <Link
                to={`/movements/${item.id}/edit`}
                aria-label={`Edit ${MOVEMENT_TYPE_LABEL[item.movementType]} on ${dateLabel}`}
                className="block px-4 py-3 focus:outline-none"
              >
                <div className="flex items-center justify-between gap-2 pr-10">
                  <div className="flex min-w-0 items-center gap-2 text-xs text-muted-foreground">
                    {typePill(item)}
                    <span className="truncate">{dateLabel}</span>
                  </div>
                  <Numeric className={cn('shrink-0 text-sm font-medium', amountColor(item))}>
                    {item.currencySymbol} {formatNumberForDisplay(item.amount, numberFormat)}
                  </Numeric>
                </div>
                <div className="mt-1.5 truncate pr-28 text-sm font-medium">
                  {primary}
                </div>
                {showAccountLine && (
                  <div className="mt-1.5 truncate pr-28 text-xs text-muted-foreground">
                    {account}
                  </div>
                )}
              </Link>
              <div
                className="absolute right-2 top-2 flex min-h-11 min-w-11 items-center justify-center"
                onClick={(e) => e.stopPropagation()}
              >
                <MovementRowMenu
                  movementId={item.id}
                  movementType={item.movementType}
                  isOpeningBalance={item.isOpeningBalance}
                  onDeleted={onRefetch}
                />
              </div>
              <div
                className="absolute right-3 bottom-2 flex min-h-11 min-w-11 items-center justify-end"
                onClick={(e) => e.stopPropagation()}
              >
                <MovementClearedToggle
                  id={item.id}
                  type={item.movementType}
                  isCleared={item.isCleared}
                />
              </div>
            </article>
          </li>
        );
      })}
    </ul>
  );
}
