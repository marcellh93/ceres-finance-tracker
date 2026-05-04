import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { usePagination } from '@/hooks/usePagination';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { REPORTS_NET_WORTH_URL, reportMetaBySlug, type NetWorthEntryDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportLocalFilterBar } from './ReportLocalFilterBar';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

const PAGE_SIZE = 10;

export function NetWorth() {
  const { filters, toQueryString } = useReportsFilters();
  const { data, error, loading, refetch } = useApi<NetWorthEntryDto[]>(REPORTS_NET_WORTH_URL);
  const { paginatedItems, currentPage, totalPages, next, prev } = usePagination(data ?? [], PAGE_SIZE);

  return (
    <div className="space-y-6">
      <div>
        <ReportHeader title="Net Worth" description={reportMetaBySlug('net-worth')?.description} filters={filters} showPeriod={false} />
        <ReportLocalFilterBar />
      </div>
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Net Worth" onRetry={refetch} />}
      {data && data.length === 0 && (
        <p className="text-sm text-muted-foreground">No accounts found.</p>
      )}
      {data && data.length > 0 && (
        <ReportTableCard slug="net-worth" queryString={toQueryString()} pagination={totalPages > 1 ? { currentPage, totalPages, onNext: next, onPrev: prev } : undefined}>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Currency</TableHead>
                <TableHead className="text-right">Assets</TableHead>
                <TableHead className="text-right">Liabilities</TableHead>
                <TableHead className="text-right">Net Worth</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {paginatedItems.map((row) => (
                <TableRow key={row.currencyCode}>
                  <TableCell>{row.currencyCode}</TableCell>
                  <TableCell className="text-right">
                    <Numeric>{row.currencySymbol} {row.assets.toFixed(2)}</Numeric>
                  </TableCell>
                  <TableCell className="text-right">
                    <Numeric>{row.currencySymbol} {row.liabilities.toFixed(2)}</Numeric>
                  </TableCell>
                  <TableCell className="text-right">
                    <Numeric className={row.netWorth >= 0 ? 'text-success' : 'text-destructive'}>
                      {row.currencySymbol} {row.netWorth.toFixed(2)}
                    </Numeric>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </ReportTableCard>
      )}
    </div>
  );
}
