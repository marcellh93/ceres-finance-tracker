import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { REPORTS_NET_WORTH_OVER_TIME_URL, type NetWorthSnapshotRowDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

const MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

function monthLabel(row: NetWorthSnapshotRowDto): string {
  return `${MONTHS[row.month - 1]} ${row.year}`;
}

export function NetWorthOverTime() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<NetWorthSnapshotRowDto[]>(REPORTS_NET_WORTH_OVER_TIME_URL(qs));

  return (
    <div className="space-y-6">
      <ReportHeader title="Net Worth Over Time" filters={filters} />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Net Worth Over Time" onRetry={refetch} />}
      {data && data.length === 0 && (
        <p className="text-sm text-muted-foreground">No data for this period.</p>
      )}
      {data && data.length > 0 && (
        <ReportTableCard slug="net-worth-over-time" queryString={qs}>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Month</TableHead>
                <TableHead className="text-right">Assets</TableHead>
                <TableHead className="text-right">Liabilities</TableHead>
                <TableHead className="text-right">Net Worth</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((row) => (
                <TableRow key={`${row.year}-${row.month}`}>
                  <TableCell>{monthLabel(row)}</TableCell>
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
