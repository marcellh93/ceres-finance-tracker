'use client';

import { Progress as ProgressPrimitive } from '@base-ui/react/progress';
import { Numeric } from '@/components/Numeric';
import { cn } from '@/lib/utils';
import { formatNumberForDisplay } from '../../lib/amount-format';
import { useSettings } from '../../lib/use-settings';

type Props = {
  progress: number;
  target: number;
  currencySymbol: string;
  className?: string;
};

export function BudgetProgressBar({
  progress,
  target,
  currencySymbol,
  className,
}: Props) {
  const settings = useSettings();
  const numberFormat = settings.data?.numberFormat ?? 'period_decimal';
  const rawPercent = target > 0 ? (progress / target) * 100 : 0;
  const displayPercent = Math.min(100, Math.round(rawPercent));

  const indicatorColor =
    rawPercent >= 100
      ? 'bg-destructive'
      : rawPercent >= 70
        ? 'bg-warning'
        : 'bg-success';

  return (
    <div className={cn('space-y-1', className)}>
      <div className="flex justify-between text-xs">
        <Numeric>
          {currencySymbol} {formatNumberForDisplay(progress, numberFormat)}
        </Numeric>
        <Numeric className="text-muted-foreground">
          {currencySymbol} {formatNumberForDisplay(target, numberFormat)}
        </Numeric>
      </div>
      <ProgressPrimitive.Root value={displayPercent} className="flex w-full">
        <ProgressPrimitive.Track className="relative flex h-2 w-full items-center overflow-x-hidden rounded-full bg-muted">
          <ProgressPrimitive.Indicator
            className={cn('h-full transition-all', indicatorColor)}
          />
        </ProgressPrimitive.Track>
      </ProgressPrimitive.Root>
    </div>
  );
}
