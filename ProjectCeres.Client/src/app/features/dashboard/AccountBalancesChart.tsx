import { Bar, BarChart, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { chartColors } from '../../lib/chart-colors';
import { CardError } from '../../components/CardError';
import { ACCOUNT_BALANCES_URL, type AccountBalancesDto } from './charts-api';

const SLOT_COUNT = 8;
type Slot = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8;

export function AccountBalancesChart() {
  const { data, error, loading, refetch } = useApi<AccountBalancesDto>(ACCOUNT_BALANCES_URL);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Account Balances</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && <Skeleton className="h-[220px] w-full" />}
        {error && <CardError section="Account Balances" onRetry={refetch} />}
        {data && data.rows.length === 0 && (
          <p className="text-sm text-muted-foreground">No data yet.</p>
        )}
        {data && data.rows.length > 0 && (
          <ResponsiveContainer width="100%" height={220} minWidth={0}>
            <BarChart data={data.rows} layout="vertical" margin={{ left: 12, right: 12 }}>
              <XAxis type="number" tick={{ fontSize: 12 }} />
              <YAxis type="category" dataKey="accountName" tick={{ fontSize: 12 }} width={100} />
              <Tooltip formatter={(v) => `${data.currencySymbol} ${Number(v ?? 0).toFixed(2)}`} />
              <Bar dataKey="balance" name="Balance">
                {data.rows.map((_, i) => (
                  <Cell key={i} fill={chartColors.slot((((i % SLOT_COUNT) + 1) as Slot))} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  );
}
