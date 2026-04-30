import { Bar, BarChart, ResponsiveContainer, Tooltip, XAxis, YAxis, Legend } from 'recharts';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { useApi } from '../../lib/use-api';
import { chartColors } from '../../lib/chart-colors';
import { formatMonth } from '../../lib/format-month';
import { CardError } from '../../components/CardError';
import { INCOME_EXPENSE_URL, type IncomeExpenseDto } from './charts-api';

export function IncomeExpenseChart() {
  const { data, error, loading, refetch } = useApi<IncomeExpenseDto>(INCOME_EXPENSE_URL);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Income vs Expense</CardTitle>
        <p className="text-xs text-muted-foreground">Last 12 months</p>
      </CardHeader>
      <CardContent>
        {loading && <Skeleton className="h-[220px] w-full" />}
        {error && <CardError section="Income vs Expense" onRetry={refetch} />}
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
              <Legend />
              <Bar dataKey="income" fill={chartColors.income} name="Income" />
              <Bar dataKey="expenses" fill={chartColors.expense} name="Expenses" />
            </BarChart>
          </ResponsiveContainer>
        )}
      </CardContent>
    </Card>
  );
}
