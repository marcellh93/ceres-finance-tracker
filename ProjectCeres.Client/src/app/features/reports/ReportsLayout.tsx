import { Outlet, Link, useMatch } from 'react-router-dom';
import { ChevronLeft } from 'lucide-react';
import { ReportsFilterBar } from './ReportsFilterBar';

export function ReportsLayout() {
  const onDetailPage = useMatch('/reports/:slug');

  return (
    <div className="mx-auto max-w-4xl">
      {onDetailPage && (
        <Link
          to="/reports"
          className="mb-2 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground transition-colors"
        >
          <ChevronLeft className="h-4 w-4" aria-hidden="true" />
          Reports
        </Link>
      )}
      <div className="space-y-6">
        <ReportsFilterBar />
        <Outlet />
      </div>
    </div>
  );
}
