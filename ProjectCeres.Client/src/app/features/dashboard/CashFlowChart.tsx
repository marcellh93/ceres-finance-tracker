import { Bar, BarChart, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { chartColors } from '../../lib/chart-colors';
import { formatMonth } from '../../lib/format-month';
import { CardError } from './CardError';
import { CASH_FLOW_URL, type CashFlowDto } from './charts-api';

export function CashFlowChart() {
  const { data, error, loading, refetch } = useApi<CashFlowDto>(CASH_FLOW_URL);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Cash Flow</CardTitle>
        <p className="text-xs text-muted-foreground">Last 12 months</p>
      </CardHeader>
      <CardContent>
        {loading && <Skeleton className="h-[220px] w-full" />}
        {error && <CardError section="Cash Flow" onRetry={refetch} />}
        {data && data.points.length === 0 && (
          <p className="text-sm text-muted-foreground">No data yet.</p>
        )}
        {data && data.points.length > 0 && (
          <ResponsiveContainer width="100%" height={220}>
            <BarChart data={data.points}>
              <XAxis dataKey="month" tick={{ fontSize: 12 }} tickFormatter={formatMonth} />
              <YAxis tick={{ fontSize: 12 }} />
              <Tooltip
                labelFormatter={formatMonth}
                formatter={(v) => `${data.currencySymbol} ${Number(v ?? 0).toFixed(2)}`}
              />
              <Bar dataKey="netFlow" name="Net Flow">
                {data.points.map((p, i) => (
                  <Cell key={i} fill={p.netFlow >= 0 ? chartColors.income : chartColors.expense} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  );
}
