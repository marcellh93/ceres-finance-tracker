import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Numeric } from '@/components/Numeric';
import { StatRow } from '@/components/StatRow';
import { useApi } from '../../lib/use-api';
import { useDelayedLoading } from '../../lib/use-delayed-loading';
import { CardError } from '../../components/CardError';
import { DataTransition, type DataTransitionState } from '../../components/DataTransition';
import { SUMMARY_URL, type NetWorthEntry, type SummaryDto } from './api';

export function NetWorthCard() {
  const { data, error, loading, refetch } = useApi<SummaryDto>(SUMMARY_URL);

  const showSkeleton = useDelayedLoading(loading && !data);
  let state: DataTransitionState;
  if (showSkeleton && !data) state = 'skeleton';
  else if (error && !data) state = 'error';
  else state = 'data';

  const skeleton = (
    <div className="space-y-2">
      <Skeleton className="h-5 w-32" />
      <Skeleton className="h-5 w-32" />
      <Skeleton className="h-5 w-32" />
    </div>
  );

  return (
    <Card>
      <CardHeader>
        <CardTitle>Net Worth</CardTitle>
      </CardHeader>
      <CardContent>
        <DataTransition
          state={state}
          skeleton={skeleton}
          error={<CardError section="Net Worth" onRetry={refetch} />}
        >
          {data && data.netWorth.length === 0 && (
            <p className="text-sm text-muted-foreground">
              No accounts yet. Add one to see your net worth.
            </p>
          )}
          {data && data.netWorth.length === 1 && <SingleCurrency entry={data.netWorth[0]} />}
          {data && data.netWorth.length > 1 && <MultiCurrency entries={data.netWorth} />}
        </DataTransition>
      </CardContent>
    </Card>
  );
}

function netWorthClass(value: number): string {
  return value >= 0 ? 'text-success' : 'text-destructive';
}

function SingleCurrency({ entry }: { entry: NetWorthEntry }) {
  return (
    <dl className="space-y-6">
      <StatRow
        label="Assets"
        value={<Numeric>{entry.currencySymbol} {entry.assets.toFixed(2)}</Numeric>}
      />
      <StatRow
        label="Liabilities"
        value={<Numeric>{entry.currencySymbol} {entry.liabilities.toFixed(2)}</Numeric>}
      />
      <StatRow
        label="Net Worth"
        value={
          <Numeric className={netWorthClass(entry.netWorth)}>
            {entry.currencySymbol} {entry.netWorth.toFixed(2)}
          </Numeric>
        }
      />
    </dl>
  );
}

function MultiCurrency({ entries }: { entries: NetWorthEntry[] }) {
  return (
    <table className="w-full text-sm">
      <thead>
        <tr className="border-b border-border text-left text-muted-foreground">
          <th className="py-2 px-2">Currency</th>
          <th className="py-2 px-2 text-right">Assets</th>
          <th className="py-2 px-2 text-right">Liabilities</th>
          <th className="py-2 px-2 text-right">Net Worth</th>
        </tr>
      </thead>
      <tbody>
        {entries.map((entry) => (
          <tr key={entry.currencyCode} className="border-b border-border last:border-0 even:bg-muted/30">
            <td className="py-2 px-2">{entry.currencyCode}</td>
            <td className="py-2 px-2 text-right"><Numeric>{entry.assets.toFixed(2)}</Numeric></td>
            <td className="py-2 px-2 text-right"><Numeric>{entry.liabilities.toFixed(2)}</Numeric></td>
            <td className="py-2 px-2 text-right">
              <Numeric className={netWorthClass(entry.netWorth)}>
                {entry.currencySymbol} {entry.netWorth.toFixed(2)}
              </Numeric>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
