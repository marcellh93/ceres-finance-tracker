import { type ReactNode } from 'react';
import { cn } from '@/lib/utils';

type StatTileProps = {
  /** Short uppercase descriptor shown above the value. */
  label: string;
  /** The value. Pass <Numeric>...</Numeric> for tabular numerics. */
  value: ReactNode;
  /** Optional className applied to the value's wrapping div (color, weight, size). */
  valueClassName?: string;
};

/**
 * Vertical KPI tile. Use for prominent metrics where the value
 * deserves visual weight (dashboards, summary blocks).
 */
export function StatTile({ label, value, valueClassName }: StatTileProps) {
  return (
    <div className="flex flex-col gap-1.5">
      <div className="text-xs uppercase tracking-wider text-muted-foreground font-medium">
        {label}
      </div>
      <div className={cn(valueClassName)}>{value}</div>
    </div>
  );
}
