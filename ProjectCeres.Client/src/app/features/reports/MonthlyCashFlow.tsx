import { Skeleton } from '@/components/ui/skeleton';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { Numeric } from '@/components/Numeric';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { REPORTS_MONTHLY_CASH_FLOW_URL, type MonthlyCashFlowRowDto } from './reports-api';
import { ReportHeader } from './ReportHeader';
import { ReportTableCard } from './ReportTableCard';
import { useReportsFilters } from './useReportsFilters';

const MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

export function MonthlyCashFlow() {
  const { filters, toQueryString } = useReportsFilters();
  const qs = toQueryString();
  const { data, error, loading, refetch } = useApi<MonthlyCashFlowRowDto[]>(REPORTS_MONTHLY_CASH_FLOW_URL(qs));

  return (
    <div className="space-y-6">
      <ReportHeader title="Monthly Cash Flow" filters={filters} />
      {loading && <Skeleton className="h-[400px] w-full" />}
      {error && <CardError section="Monthly Cash Flow" onRetry={refetch} />}
      {data && data.length === 0 && (
        <p className="text-sm text-muted-foreground">No data for this period.</p>
      )}
      {data && data.length > 0 && (
        <ReportTableCard slug="monthly-cash-flow" queryString={qs}>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Month</TableHead>
                <TableHead className="text-right">Income</TableHead>
                <TableHead className="text-right">Expenses</TableHead>
                <TableHead className="text-right">Net</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((row) => (
                <TableRow key={`${row.year}-${row.month}`}>
                  <TableCell>{MONTHS[row.month - 1]} {row.year}</TableCell>
                  <TableCell className="text-right">
                    <Numeric className="text-success">{row.currencySymbol} {row.totalIncome.toFixed(2)}</Numeric>
                  </TableCell>
                  <TableCell className="text-right">
                    <Numeric className="text-destructive">{row.currencySymbol} {row.totalExpenses.toFixed(2)}</Numeric>
                  </TableCell>
                  <TableCell className="text-right">
                    <Numeric className={row.net >= 0 ? 'text-success' : 'text-destructive'}>
                      {row.currencySymbol} {row.net.toFixed(2)}
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
