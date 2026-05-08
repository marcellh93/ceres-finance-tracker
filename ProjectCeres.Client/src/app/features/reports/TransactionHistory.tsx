import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { Badge } from '@/components/ui/badge';
import { useDocumentTitle } from '../../lib/use-document-title';
import { usePagination } from '@/hooks/usePagination';
import { CardError } from '../../components/CardError';
import { DataTransition, type DataTransitionState } from '../../components/DataTransition';
import { useApi } from '../../lib/use-api';
import { useDelayedLoading } from '../../lib/use-delayed-loading';
import { useSettings } from '../../lib/use-settings';
import { formatDate } from '../../lib/date-format';
import { REPORTS_TRANSACTION_HISTORY_URL, type TransactionHistoryRowDto } from './reports-api';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

const PAGE_SIZE = 50;

export function TransactionHistory() {
  useDocumentTitle('Transaction History');
  const { toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<TransactionHistoryRowDto[]>(REPORTS_TRANSACTION_HISTORY_URL(qs));
  const { data: settings } = useSettings();
  const { paginatedItems, currentPage, totalPages, next, prev } = usePagination(data ?? [], PAGE_SIZE);

  const showSkeleton = useDelayedLoading(loading && !data);
  let state: DataTransitionState;
  if (showSkeleton && !data) state = 'skeleton';
  else if (error && !data) state = 'error';
  else state = 'data';

  const skeleton = <Skeleton className="h-[400px] w-full" />;
  const errorSlot = <CardError section="Transaction History" onRetry={refetch} />;

  return (
    <div className="space-y-6">
      <DataTransition state={state} skeleton={skeleton} error={errorSlot}>
      {data && data.length === 0 && (
        <p className="text-sm text-muted-foreground">No transactions for this period.</p>
      )}
      {data && data.length > 0 && (
        <ReportTableCard slug="transaction-history" queryString={qs} pagination={totalPages > 1 ? { currentPage, totalPages, onNext: next, onPrev: prev } : undefined}>
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
              {paginatedItems.map((row) => (
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
      )}
      </DataTransition>
    </div>
  );
}
