import type { ReactNode } from 'react';
import { cn } from '@/lib/utils';

type TileProps = {
  children: ReactNode;
  className?: string;
};

/**
 * KPI surface wrapper. Use for grouping small numeric tiles
 * (e.g. inside MTD card). Applies a muted surface, rounded
 * corners, and consistent padding.
 */
export function Tile({ children, className }: TileProps) {
  return <div className={cn('rounded-md bg-muted/40 p-4', className)}>{children}</div>;
}
