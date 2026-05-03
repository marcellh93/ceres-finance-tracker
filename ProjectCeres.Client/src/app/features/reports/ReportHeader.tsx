import { useEffect, useRef } from 'react';
import type { ReportsFilters } from './useReportsFilters';
import { formatDate } from '../../lib/date-format';
import { useSettings } from '../../lib/use-settings';

type Props = {
  title: string;
  filters: ReportsFilters;
  showPeriod?: boolean;
};

export function ReportHeader({ title, filters, showPeriod = true }: Props) {
  const headingRef = useRef<HTMLHeadingElement>(null);
  const { data: settings } = useSettings();

  useEffect(() => { headingRef.current?.focus(); }, []);

  const periodSummary =
    showPeriod && filters.from && filters.to
      ? `${formatDate(filters.from, settings?.dateFormat)} – ${formatDate(filters.to, settings?.dateFormat)}`
      : null;

  return (
    <div className="space-y-1" style={{ viewTransitionName: `report-header-${title.toLowerCase().replace(/\s+/g, '-')}` }}>
      <h1
        ref={headingRef}
        tabIndex={-1}
        className="text-2xl font-semibold outline-none"
      >
        {title}
      </h1>
      {periodSummary && (
        <p className="text-sm text-muted-foreground">{periodSummary}</p>
      )}
    </div>
  );
}
