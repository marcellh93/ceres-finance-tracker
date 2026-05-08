import { Outlet, useMatch } from 'react-router-dom';
import { useDocumentTitle } from '../../lib/use-document-title';
import { ReportsTabBar } from './ReportsTabBar';
import { ReportsSharedFilterBar } from './ReportsSharedFilterBar';
import { ReportLocalFilterBar } from './ReportLocalFilterBar';
import { ReportHeader } from './ReportHeader';
import { reportMetaBySlug } from './reports-api';
import { useReportsFilters } from './useReportsFilters';

export function ReportsLayout() {
  useDocumentTitle('Reports');
  const match = useMatch('/reports/:slug');
  const slug = match?.params.slug ?? '';
  const meta = reportMetaBySlug(slug);
  const { filters } = useReportsFilters();

  return (
    <div className="flex flex-col">
      <div className="sticky top-0 z-10 -mx-6 -mt-6 bg-background">
        <ReportsTabBar />
        <div className="mx-6">
          {meta && (
            <div className="px-[8%] pt-4 pb-2">
              <ReportHeader
                title={meta.label}
                description={meta.description}
                filters={filters}
                showPeriod={slug !== 'net-worth'}
              />
            </div>
          )}
          <ReportsSharedFilterBar />
          <ReportLocalFilterBar />
        </div>
      </div>
      <div className="px-[8%] pt-10 pb-6">
        <Outlet />
      </div>
    </div>
  );
}
