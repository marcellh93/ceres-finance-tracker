import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Numeric } from '@/components/Numeric';
import { StatRow } from '@/components/StatRow';
import { useApi } from '../../lib/use-api';
import { CardError } from './CardError';
import { SUMMARY_URL, type SummaryDto } from './api';

function formatPercent(fraction: number): string {
  return `${(fraction * 100).toFixed(1)}%`;
}

export function MtdCard() {
  const { data, error, loading, refetch } = useApi<SummaryDto>(SUMMARY_URL);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Month to Date</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && (
          <div className="space-y-2">
            <Skeleton className="h-5 w-32" />
            <Skeleton className="h-5 w-32" />
            <Skeleton className="h-5 w-32" />
          </div>
        )}
        {error && <CardError section="Month to Date" onRetry={refetch} />}
        {data && data.mtd.income === 0 && data.mtd.expenses === 0 && (
          <p className="text-sm text-muted-foreground">No transactions this month yet.</p>
        )}
        {data && (data.mtd.income !== 0 || data.mtd.expenses !== 0) && (
          <dl className="space-y-6">
            <StatRow
              label="Income"
              value={<Numeric className="text-success">{data.mtd.currencySymbol} {data.mtd.income.toFixed(2)}</Numeric>}
            />
            <StatRow
              label="Expenses"
              value={<Numeric className="text-destructive">{data.mtd.currencySymbol} {data.mtd.expenses.toFixed(2)}</Numeric>}
            />
            <StatRow
              label="Savings Rate"
              value={<Numeric>{formatPercent(data.mtd.savingsRate)}</Numeric>}
            />
          </dl>
        )}
      </CardContent>
    </Card>
  );
}
