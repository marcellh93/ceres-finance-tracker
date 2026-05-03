import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { Badge } from '@/components/ui/badge';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { useSettings } from '../../lib/use-settings';
import { formatDate } from '../../lib/date-format';
import { REPORTS_TRANSACTION_HISTORY_URL, type TransactionHistoryRowDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

const PAGE_SIZE = 50;

export function TransactionHistory() {
  const { filters, setFilter, toQueryString } = useReportsFilters();
  const page = filters.page ?? 1;
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<TransactionHistoryRowDto[]>(REPORTS_TRANSACTION_HISTORY_URL(qs));
  const { data: settings } = useSettings();

  const hasNext = (data?.length ?? 0) === PAGE_SIZE;
  const hasPrev = page > 1;

  return (
    <div className="space-y-6">
      <ReportHeader title="Transaction History" filters={filters} />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Transaction History" onRetry={refetch} />}
      {data && data.length === 0 && (
        <p className="text-sm text-muted-foreground">No transactions for this period.</p>
      )}
      {data && data.length > 0 && (
        <>
          <ReportTableCard slug="transaction-history" queryString={qs}>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Date</TableHead>
                  <TableHead>Description</TableHead>
                  <TableHead>Account</TableHead>
                  <TableHead>Category</TableHead>
                  <TableHead>Type</TableHead>
                  <TableHead className="text-right">Amount</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.map((row) => (
                  <TableRow key={row.id}>
                    <TableCell className="whitespace-nowrap">
                      <Numeric>{formatDate(row.date, settings?.dateFormat)}</Numeric>
                    </TableCell>
                    <TableCell>{row.description ?? '—'}</TableCell>
                    <TableCell className="text-muted-foreground">{row.accountName}</TableCell>
                    <TableCell className="text-muted-foreground">{row.categoryName}</TableCell>
                    <TableCell>
                      <Badge variant={row.categoryTypeName === 'Income' ? 'success' : 'secondary'}>
                        {row.categoryTypeName}
                      </Badge>
                    </TableCell>
                    <TableCell className="text-right">
                      <Numeric className={row.categoryTypeName === 'Income' ? 'text-success' : 'text-destructive'}>
                        {row.currencySymbol} {row.amount.toFixed(2)}
                      </Numeric>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </ReportTableCard>
          <div className="flex items-center justify-between">
            <p className="text-sm text-muted-foreground">Page {page}</p>
            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                disabled={!hasPrev}
                onClick={() => setFilter('page', page - 1)}
              >
                Previous
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={!hasNext}
                onClick={() => setFilter('page', page + 1)}
                aria-label="Next page"
              >
                Next
              </Button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
