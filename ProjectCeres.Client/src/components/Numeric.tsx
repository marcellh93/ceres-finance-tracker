import { type ElementType, type HTMLAttributes } from 'react';
import { cn } from '@/lib/utils';

type NumericProps = HTMLAttributes<HTMLElement> & {
  /** HTML element to render. Defaults to `span`. Use `td` inside tables. */
  as?: ElementType;
};

/**
 * Tabular numeric text. Use for currency, percentages, and dates in
 * data contexts (KPI cards, table cells, chart axes). Do NOT use for
 * percentages embedded in prose — keep those in the surrounding font.
 */
export function Numeric({ as, className, children, ...rest }: NumericProps) {
  const Tag = (as ?? 'span') as ElementType;
  return (
    <Tag className={cn('font-mono tabular-nums', className)} {...rest}>
      {children}
    </Tag>
  );
}
