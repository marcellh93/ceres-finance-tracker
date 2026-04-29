import { type ReactNode } from 'react';
import { cn } from '@/lib/utils';

type StatRowProps = {
  label: string;
  value: ReactNode;
  /** Optional className applied to the value's wrapping span. */
  valueClassName?: string;
};

/**
 * Inline labelled value. Label on the left, value on the right,
 * baseline-aligned. Wrap multiple StatRows in a `<dl className="space-y-2">`
 * for grouped stat displays (e.g. MTD card).
 */
export function StatRow({ label, value, valueClassName }: StatRowProps) {
  return (
    <div className="flex items-baseline justify-between text-sm">
      <span className="text-muted-foreground">{label}</span>
      <span className={cn(valueClassName)}>{value}</span>
    </div>
  );
}
