import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Numeric } from '@/components/Numeric';
import { StatTile } from '@/components/StatTile';
import { Tile } from '@/components/Tile';
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
          <dl className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <Tile>
              <StatTile
                label="Income"
                value={
                  <Numeric className="text-2xl text-success">
                    {data.mtd.currencySymbol} {data.mtd.income.toFixed(2)}
                  </Numeric>
                }
              />
            </Tile>
            <Tile>
              <StatTile
                label="Expenses"
                value={
                  <Numeric className="text-2xl text-destructive">
                    {data.mtd.currencySymbol} {data.mtd.expenses.toFixed(2)}
                  </Numeric>
                }
              />
            </Tile>
            <Tile>
              <StatTile
                label="Savings Rate"
                value={<Numeric className="text-2xl">{formatPercent(data.mtd.savingsRate)}</Numeric>}
              />
            </Tile>
          </dl>
        )}
      </CardContent>
    </Card>
  );
}
