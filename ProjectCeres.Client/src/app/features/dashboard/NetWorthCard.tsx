import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Numeric } from '@/components/Numeric';
import { useApi } from '../../lib/use-api';
import { CardError } from './CardError';
import { SUMMARY_URL, type NetWorthEntry, type SummaryDto } from './api';

export function NetWorthCard() {
  const { data, error, loading, refetch } = useApi<SummaryDto>(SUMMARY_URL);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Net Worth</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && (
          <div className="space-y-2">
            <Skeleton className="h-5 w-32" />
            <Skeleton className="h-5 w-32" />
            <Skeleton className="h-5 w-32" />
          </div>
        )}
        {error && <CardError section="Net Worth" onRetry={refetch} />}
        {data && data.netWorth.length === 0 && (
          <p className="text-sm text-muted-foreground">
            No accounts yet. Add one to see your net worth.
          </p>
        )}
        {data && data.netWorth.length === 1 && <SingleCurrency entry={data.netWorth[0]} />}
        {data && data.netWorth.length > 1 && <MultiCurrency entries={data.netWorth} />}
      </CardContent>
    </Card>
  );
}

function netWorthClass(value: number): string {
  return value >= 0 ? 'text-success' : 'text-destructive';
}

function SingleCurrency({ entry }: { entry: NetWorthEntry }) {
  return (
    <dl className="space-y-2 text-sm">
      <div className="flex items-baseline justify-between">
        <dt className="text-muted-foreground">Assets</dt>
        <dd><Numeric>{entry.currencySymbol} {entry.assets.toFixed(2)}</Numeric></dd>
      </div>
      <div className="flex items-baseline justify-between">
        <dt className="text-muted-foreground">Liabilities</dt>
        <dd><Numeric>{entry.currencySymbol} {entry.liabilities.toFixed(2)}</Numeric></dd>
      </div>
      <div className="flex items-baseline justify-between">
        <dt className="text-muted-foreground">Net Worth</dt>
        <dd>
          <Numeric className={netWorthClass(entry.netWorth)}>
            {entry.currencySymbol} {entry.netWorth.toFixed(2)}
          </Numeric>
        </dd>
      </div>
    </dl>
  );
}

function MultiCurrency({ entries }: { entries: NetWorthEntry[] }) {
  return (
    <table className="w-full text-sm">
      <thead>
        <tr className="border-b border-border text-left text-muted-foreground">
          <th className="py-2 font-normal">Currency</th>
          <th className="py-2 text-right font-normal">Assets</th>
          <th className="py-2 text-right font-normal">Liabilities</th>
          <th className="py-2 text-right font-normal">Net Worth</th>
        </tr>
      </thead>
      <tbody>
        {entries.map((entry) => (
          <tr key={entry.currencyCode} className="border-b border-border last:border-0">
            <td className="py-2">{entry.currencyCode}</td>
            <td className="py-2 text-right"><Numeric>{entry.assets.toFixed(2)}</Numeric></td>
            <td className="py-2 text-right"><Numeric>{entry.liabilities.toFixed(2)}</Numeric></td>
            <td className="py-2 text-right">
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
