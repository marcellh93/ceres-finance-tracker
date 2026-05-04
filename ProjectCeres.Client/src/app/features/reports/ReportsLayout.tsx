import { Outlet, useMatch } from 'react-router-dom';
import { ReportsTabBar } from './ReportsTabBar';
import { ReportsSharedFilterBar } from './ReportsSharedFilterBar';
import { ReportHeader } from './ReportHeader';
import { reportMetaBySlug } from './reports-api';
import { useReportsFilters } from './useReportsFilters';

export function ReportsLayout() {
  const match = useMatch('/reports/:slug');
  const slug = match?.params.slug ?? '';
  const meta = reportMetaBySlug(slug);
  const { filters } = useReportsFilters();

  return (
    <div className="flex flex-col">
      <ReportsTabBar />
      <div className="px-[8%] pt-6 pb-2">
        {meta && (
          <ReportHeader
            title={meta.label}
            description={meta.description}
            filters={filters}
            showPeriod={slug !== 'net-worth'}
          />
        )}
      </div>
      <ReportsSharedFilterBar />
      <div className="px-[8%] py-6">
        <Outlet />
      </div>
    </div>
  );
}
