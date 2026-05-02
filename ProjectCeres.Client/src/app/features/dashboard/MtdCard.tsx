import { ArrowDown, ArrowUp, Minus } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Numeric } from '@/components/Numeric';
import { StatTile } from '@/components/StatTile';
import { Tile } from '@/components/Tile';
import { useApi } from '../../lib/use-api';
import { CardError } from '../../components/CardError';
import { formatNumberForDisplay } from '../../lib/amount-format';
import { useSettings } from '../../lib/use-settings';
import { type NumberFormat } from '../../lib/amount-format';
import { SUMMARY_URL, type SummaryDto } from './api';

function formatPercent(fraction: number): string {
  return `${(fraction * 100).toFixed(1)}%`;
}

type ComparisonDirection = 'up' | 'down' | 'flat';

/**
 * Inline "vs €X last period" comparison rendered below a tile's value.
 *
 * `goodWhenUp` = true for Income, Savings Rate (more is better → green when up).
 * `goodWhenUp` = false for Expenses (more is worse → red when up).
 *
 * When `prior` is null, renders the muted "no prior period" copy.
 */
function PriorComparison({
  current,
  prior,
  formatValue,
  goodWhenUp,
}: {
  current: number;
  prior: number | null;
  formatValue: (n: number) => string;
  goodWhenUp: boolean;
}) {
  if (prior === null) {
    return (
      <div className="mt-1 text-xs text-muted-foreground">
        No prior period to compare
      </div>
    );
  }

  const direction: ComparisonDirection =
    current > prior ? 'up' : current < prior ? 'down' : 'flat';
  const isGood =
    direction === 'flat' ? null : (direction === 'up') === goodWhenUp;

  const colorClass =
    isGood === null ? 'text-muted-foreground'
    : isGood          ? 'text-success'
                      : 'text-destructive';

  const Icon =
    direction === 'up'   ? ArrowUp
    : direction === 'down' ? ArrowDown
                           : Minus;

  return (
    <div className={`mt-1 flex items-center gap-1 text-xs ${colorClass}`}>
      <Icon className="h-3 w-3" aria-hidden="true" />
      <span>vs {formatValue(prior)} last period</span>
    </div>
  );
}

function formatMoney(n: number, sym: string, fmt: NumberFormat): string {
  return `${sym} ${formatNumberForDisplay(n, fmt)}`;
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
        {data && (data.mtd.income !== 0 || data.mtd.expenses !== 0) && (() => {
          const netFlow = data.mtd.income - data.mtd.expenses;
          // Net Flow comparison is only meaningful when BOTH prior totals exist.
          const priorNetFlow =
            data.mtd.priorPeriodIncome !== null && data.mtd.priorPeriodExpenses !== null
              ? data.mtd.priorPeriodIncome - data.mtd.priorPeriodExpenses
              : null;
          const netFlowColor = netFlow >= 0 ? 'text-success' : 'text-destructive';
          const netFlowSign = netFlow >= 0 ? '+' : '';

          return (
          <dl className="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <Tile>
              <StatTile
                label="Income"
                value={
                  <div>
                    <Numeric className="text-xl text-success whitespace-nowrap">
                      {data.mtd.currencySymbol} {formatNumberForDisplay(data.mtd.income, numberFormat)}
                    </Numeric>
                    <PriorComparison
                      current={data.mtd.income}
                      prior={data.mtd.priorPeriodIncome}
                      formatValue={(n) => formatMoney(n, data.mtd.currencySymbol, numberFormat)}
                      goodWhenUp
                    />
                  </div>
                }
              />
            </Tile>
            <Tile>
              <StatTile
                label="Expenses"
                value={
                  <div>
                    <Numeric className="text-xl text-destructive whitespace-nowrap">
                      {data.mtd.currencySymbol} {formatNumberForDisplay(data.mtd.expenses, numberFormat)}
                    </Numeric>
                    <PriorComparison
                      current={data.mtd.expenses}
                      prior={data.mtd.priorPeriodExpenses}
                      formatValue={(n) => formatMoney(n, data.mtd.currencySymbol, numberFormat)}
                      goodWhenUp={false}
                    />
                  </div>
                }
              />
            </Tile>
            <Tile>
              <StatTile
                label="Net Flow"
                value={
                  <div>
                    <Numeric className={`text-xl whitespace-nowrap ${netFlowColor}`}>
                      {netFlowSign}{data.mtd.currencySymbol} {formatNumberForDisplay(Math.abs(netFlow), numberFormat)}
                    </Numeric>
                    <PriorComparison
                      current={netFlow}
                      prior={priorNetFlow}
                      formatValue={(n) =>
                        `${n >= 0 ? '+' : '−'}${data.mtd.currencySymbol} ${formatNumberForDisplay(Math.abs(n), numberFormat)}`
                      }
                      goodWhenUp
                    />
                  </div>
                }
              />
            </Tile>
            <Tile>
              <StatTile
                label="Savings Rate"
                value={
                  <div>
                    <Numeric className="text-xl whitespace-nowrap">
                      {formatPercent(data.mtd.savingsRate)}
                    </Numeric>
                    <PriorComparison
                      current={data.mtd.savingsRate}
                      prior={data.mtd.priorPeriodSavingsRate}
                      formatValue={formatPercent}
                      goodWhenUp
                    />
                  </div>
                }
              />
            </Tile>
          </dl>
          );
        })()}
      </CardContent>
    </Card>
  );
}
