import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Numeric } from '@/components/Numeric';
import { StatTile } from '@/components/StatTile';
import { Tile } from '@/components/Tile';
import { useApi } from '../../lib/use-api';
import { CardError } from '../../components/CardError';
import { formatNumberForDisplay } from '../../lib/amount-format';
import { useSettings } from '../../lib/use-settings';
import { SUMMARY_URL, type SummaryDto } from './api';

function formatPercent(fraction: number): string {
  return `${(fraction * 100).toFixed(1)}%`;
}

export function MtdCard() {
  const { data, error, loading, refetch } = useApi<SummaryDto>(SUMMARY_URL);
  const settings = useSettings();
  const numberFormat = settings.data?.numberFormat ?? 'period_decimal';

  return (
    <Card>
      <CardHeader>
        <CardTitle>Cycle to Date</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && (
          <div className="space-y-2">
            <Skeleton className="h-5 w-32" />
            <Skeleton className="h-5 w-32" />
            <Skeleton className="h-5 w-32" />
          </div>
        )}
        {error && <CardError section="Cycle to Date" onRetry={refetch} />}
        {data && data.mtd.income === 0 && data.mtd.expenses === 0 && (
          <p className="text-sm text-muted-foreground">No transactions this period yet.</p>
        )}
        {data && (data.mtd.income !== 0 || data.mtd.expenses !== 0) && (
          <dl className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <Tile>
              <StatTile
                label="Income"
                value={
                  <Numeric className="text-xl text-success whitespace-nowrap">
                    {data.mtd.currencySymbol} {formatNumberForDisplay(data.mtd.income, numberFormat)}
                  </Numeric>
                }
              />
            </Tile>
            <Tile>
              <StatTile
                label="Expenses"
                value={
                  <Numeric className="text-xl text-destructive whitespace-nowrap">
                    {data.mtd.currencySymbol} {formatNumberForDisplay(data.mtd.expenses, numberFormat)}
                  </Numeric>
                }
              />
            </Tile>
            <Tile className="sm:col-span-2">
              <StatTile
                label="Savings Rate"
                value={<Numeric className="text-xl whitespace-nowrap">{formatPercent(data.mtd.savingsRate)}</Numeric>}
              />
            </Tile>
          </dl>
        )}
      </CardContent>
    </Card>
  );
}
