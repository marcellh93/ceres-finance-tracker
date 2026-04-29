import { Bar, BarChart, Cell, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { chartColors } from '../../lib/chart-colors';
import { CardError } from './CardError';
import { SPENDING_BY_CATEGORY_URL, type SpendingByCategoryDto } from './charts-api';

const SLOT_COUNT = 8;
type Slot = 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8;

export function SpendingByCategoryChart() {
  const { data, error, loading, refetch } = useApi<SpendingByCategoryDto>(SPENDING_BY_CATEGORY_URL);

  const top = data?.slices.slice(0, 10) ?? [];

  return (
    <Card>
      <CardHeader>
        <CardTitle>Spending by Category</CardTitle>
        <p className="text-xs text-muted-foreground">This month</p>
      </CardHeader>
      <CardContent>
        {loading && <Skeleton className="h-[220px] w-full" />}
        {error && <CardError section="Spending by Category" onRetry={refetch} />}
        {data && data.slices.length === 0 && (
          <p className="text-sm text-muted-foreground">No data yet.</p>
        )}
        {data && data.slices.length > 0 && (
          <ResponsiveContainer width="100%" height={220}>
            <BarChart data={top} layout="vertical" margin={{ left: 12, right: 12 }}>
              <XAxis type="number" tick={{ fontSize: 12 }} />
              <YAxis type="category" dataKey="categoryName" tick={{ fontSize: 12 }} width={100} />
              <Tooltip
                formatter={(v) => {
                  const amount = Number(v ?? 0);
                  const pct = data.total > 0 ? ((amount / data.total) * 100).toFixed(1) : '0.0';
                  return `${data.currencySymbol} ${amount.toFixed(2)} (${pct}%)`;
                }}
              />
              <Bar dataKey="amount" name="Amount">
                {top.map((_, i) => (
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
