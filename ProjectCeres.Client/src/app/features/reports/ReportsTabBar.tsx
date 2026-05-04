import { Link, useMatch, useSearchParams } from 'react-router-dom';
import { cn } from '@/lib/utils';
import { REPORT_META } from './reports-api';

export function ReportsTabBar() {
  const match = useMatch('/reports/:slug');
  const activeSlug = match?.params.slug ?? '';
  const [searchParams] = useSearchParams();

  return (
    <nav
      aria-label="Reports"
      className="flex overflow-x-auto border-b border-border bg-background scrollbar-none"
    >
      {REPORT_META.map((entry) => {
        const isActive = entry.slug === activeSlug;
        const qs = searchParams.toString();
        const to = qs ? `/reports/${entry.slug}?${qs}` : `/reports/${entry.slug}`;
        return (
          <Link
            key={entry.slug}
            to={to}
            aria-current={isActive ? 'page' : undefined}
            className={cn(
              'relative shrink-0 px-4 py-3 text-sm font-medium text-muted-foreground no-underline transition-colors hover:text-foreground',
              isActive && 'text-foreground after:absolute after:inset-x-4 after:bottom-0 after:h-0.5 after:rounded-t after:bg-foreground after:opacity-100',
            )}
          >
            {entry.label}
          </Link>
        );
      })}
    </nav>
  );
}
