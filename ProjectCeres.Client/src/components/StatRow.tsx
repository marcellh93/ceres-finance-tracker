import { type ReactNode } from 'react';
import { cn } from '@/lib/utils';

type StatRowProps = {
  label: string;
  value: ReactNode;
  /** Optional className applied to the label span. Use to set a fixed
   *  column width (e.g. `w-32`) so values across multiple rows align. */
  labelClassName?: string;
  /** Optional className applied to the value's wrapping span. */
  valueClassName?: string;
};

/**
 * Inline labelled value. Label on the left, value to its right, baseline-aligned.
 * Use a `labelClassName` like `w-32` to lock the label width so values across
 * multiple rows align vertically. Wrap multiple StatRows in a
 * `<dl className="space-y-2">` for grouped stat displays.
 */
export function StatRow({ label, value, labelClassName, valueClassName }: StatRowProps) {
  return (
    <div className="flex items-baseline gap-6 text-sm">
      <span className={cn('text-muted-foreground', labelClassName)}>{label}</span>
      <span className={cn(valueClassName)}>{value}</span>
    </div>
  );
}
