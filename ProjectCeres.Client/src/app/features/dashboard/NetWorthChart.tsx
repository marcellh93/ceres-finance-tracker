import { Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis, Legend } from 'recharts';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { useDelayedLoading } from '../../lib/use-delayed-loading';
import { chartColors } from '../../lib/chart-colors';
import { formatMonth } from '../../lib/format-month';
import { CardError } from '../../components/CardError';
import { DataTransition, type DataTransitionState } from '../../components/DataTransition';
import { NET_WORTH_TREND_URL, type NetWorthTrendDto } from './charts-api';

export function NetWorthChart() {
  const { data, error, loading, refetch } = useApi<NetWorthTrendDto>(NET_WORTH_TREND_URL);

  const showSkeleton = useDelayedLoading(loading && !data);
  let state: DataTransitionState;
  if (showSkeleton && !data) state = 'skeleton';
  else if (error && !data) state = 'error';
  else state = 'data';

  return (
    <Card>
      <CardHeader>
        <CardTitle>Net Worth Over Time</CardTitle>
        <p className="text-xs text-muted-foreground">Last 12 months</p>
      </CardHeader>
      <CardContent>
        <DataTransition
          state={state}
          skeleton={<Skeleton className="h-[220px] w-full" />}
          error={<CardError section="Net Worth Over Time" onRetry={refetch} />}
        >
        {data && data.points.length === 0 && (
          <p className="text-sm text-muted-foreground">No data yet.</p>
        )}
        {data && data.points.length > 0 && (
          <ResponsiveContainer width="100%" height={220} minWidth={0}>
            <LineChart data={data.points}>
              <XAxis dataKey="month" tick={{ fontSize: 12 }} tickFormatter={formatMonth} />
              <YAxis tick={{ fontSize: 12 }} />
              <Tooltip
                labelFormatter={formatMonth}
                formatter={(v) => `${data.currencySymbol} ${Number(v ?? 0).toFixed(2)}`}
              />
              <Legend />
              <Line type="monotone" dataKey="assets" stroke={chartColors.assets} name="Assets" dot={false} strokeWidth={2} />
              <Line type="monotone" dataKey="netWorth" stroke={chartColors.netWorth} name="Net Worth" dot={false} strokeWidth={2} />
            </LineChart>
          </ResponsiveContainer>
        )}
        </DataTransition>
      </CardContent>
    </Card>
  );
}
