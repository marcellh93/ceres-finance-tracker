import { type ReactNode } from 'react';
import { cn } from '@/lib/utils';

type EquationRowProps = {
  label: string;
  value: ReactNode;
  /** Optional className applied to the value's wrapping span. */
  valueClassName?: string;
};

/**
 * Compact equation row. Both label and value are muted by default
 * (the row is informational, not a headline). Use inside dense
 * vertical stacks like the Spendable Balance breakdown.
 *
 * For headline rows (e.g. "Available today" in the equation),
 * pass a valueClassName like "text-base font-bold text-success"
 * to override the muted default.
 */
export function EquationRow({ label, value, valueClassName }: EquationRowProps) {
  return (
    <div className="flex justify-between text-[11px] text-muted-foreground">
      <span>{label}</span>
      <span className={cn(valueClassName)}>{value}</span>
    </div>
  );
}
