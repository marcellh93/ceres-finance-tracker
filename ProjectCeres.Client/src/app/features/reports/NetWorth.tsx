import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { usePagination } from '@/hooks/usePagination';
import { CardError } from '../../components/CardError';
import { DataTransition, type DataTransitionState } from '../../components/DataTransition';
import { useApi } from '../../lib/use-api';
import { useDelayedLoading } from '../../lib/use-delayed-loading';
import { REPORTS_NET_WORTH_URL, type NetWorthEntryDto } from './reports-api';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

const PAGE_SIZE = 10;

export function NetWorth() {
  const { toQueryString } = useReportsFilters();
  const { data, error, loading, refetch } = useApi<NetWorthEntryDto[]>(REPORTS_NET_WORTH_URL);
  const { paginatedItems, currentPage, totalPages, next, prev } = usePagination(data ?? [], PAGE_SIZE);

  const showSkeleton = useDelayedLoading(loading && !data);
  let state: DataTransitionState;
  if (showSkeleton && !data) state = 'skeleton';
  else if (error && !data) state = 'error';
  else state = 'data';

  const skeleton = <Skeleton className="h-[400px] w-full" />;
  const errorSlot = <CardError section="Net Worth" onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <DataTransition state={state} skeleton={skeleton} error={errorSlot}>
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
      </DataTransition>
    </div>
  );
}
