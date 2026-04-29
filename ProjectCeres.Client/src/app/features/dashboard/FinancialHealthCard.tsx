import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Numeric } from '@/components/Numeric';
import { EquationRow } from '@/components/EquationRow';
import { useApi } from '../../lib/use-api';
import { CardError } from './CardError';
import { HEALTH_URL, type HealthDto } from './api';
import {
  availableTodayClass,
  burnRateClass,
  incomeDeltaClass,
  runwayClass,
  safeToSpendClass,
} from './dashboardViewHelper';

export function FinancialHealthCard() {
  const { data, error, loading, refetch } = useApi<HealthDto>(HEALTH_URL);

  return (
    <Card>
      <CardHeader>
        <CardTitle>Financial Health</CardTitle>
      </CardHeader>
      <CardContent>
        {loading && <Skeleton className="h-32 w-full" />}
        {error && <CardError section="Financial Health" onRetry={refetch} />}
        {data && (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-[1.6fr_1fr_1fr_1fr] gap-6">
            <SpendablePanel data={data} />
            <RunwayPanel data={data} />
            <IncomeDeltaPanel data={data} />
            <BurnRatePanel data={data} />
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function PanelLabel({ children }: { children: string }) {
  return (
    <div className="text-xs uppercase tracking-wider text-muted-foreground font-medium mb-2">
      {children}
    </div>
  );
}

function PanelEmpty({ children }: { children: string }) {
  return <div className="text-xs italic text-muted-foreground">{children}</div>;
}

function SpendablePanel({ data }: { data: HealthDto }) {
  return (
    <div>
      <PanelLabel>Spendable Balance</PanelLabel>
      {data.availableToday === null ? (
        <PanelEmpty>No asset accounts found</PanelEmpty>
      ) : (
        <SpendableEquation data={data} />
      )}
    </div>
  );
}

function SpendableEquation({ data }: { data: HealthDto }) {
  const sym = data.currencySymbol;
  const liquid = (data.availableToday ?? 0) + (data.imminentBills ?? 0);
  const hasImminent = (data.imminentBills ?? 0) !== 0;
  const hasLater = (data.laterBills ?? 0) !== 0;
  const hasReserve = (data.budgetReserve ?? 0) !== 0;
  const showSafeToSpend = data.safeToSpend !== null && (hasLater || hasReserve);

  return (
    <div className="space-y-1.5">
      <EquationRow label="Liquid" value={<Numeric>{sym} {liquid.toFixed(2)}</Numeric>} />
      {hasImminent && (
        <EquationRow
          label="Bills due (7 days)"
          value={<Numeric>−{sym} {(data.imminentBills ?? 0).toFixed(2)}</Numeric>}
        />
      )}
      <div className="border-t border-border mt-1 pt-2">
        <EquationRow
          label="Available today"
          value={
            <Numeric className={`text-base font-bold ${availableTodayClass(data.availableToday!)}`}>
              {sym} {data.availableToday!.toFixed(2)}
            </Numeric>
          }
        />
      </div>
      {hasLater && (
        <EquationRow
          label="Bills later this month"
          value={<Numeric>−{sym} {(data.laterBills ?? 0).toFixed(2)}</Numeric>}
        />
      )}
      {hasReserve && (
        <EquationRow
          label="Budget reserved"
          value={<Numeric>−{sym} {(data.budgetReserve ?? 0).toFixed(2)}</Numeric>}
        />
      )}
      {showSafeToSpend && (
        <div className="border-t border-border mt-1 pt-2">
          <EquationRow
            label="Safe to spend"
            value={
              <Numeric className={`text-sm font-semibold ${safeToSpendClass(data.safeToSpend!)}`}>
                {sym} {data.safeToSpend!.toFixed(2)}
              </Numeric>
            }
          />
        </div>
      )}
    </div>
  );
}

function RunwayPanel({ data }: { data: HealthDto }) {
  const sym = data.currencySymbol;
  return (
    <div className="lg:border-l lg:border-border lg:pl-6">
      <PanelLabel>Runway</PanelLabel>
      {data.runwayMonths === null ? (
        <PanelEmpty>Needs 6 months of expense history</PanelEmpty>
      ) : (
        <>
          <Numeric className={`text-2xl font-bold ${runwayClass(data.runwayMonths)}`}>
            {data.runwayMonths.toFixed(1)} mo
          </Numeric>
          {data.avgMonthlyExpense != null && (
            <div className="mt-1 text-xs text-muted-foreground">
              {'at '}
              <Numeric>{sym} {data.avgMonthlyExpense.toFixed(0)}/mo</Numeric>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function IncomeDeltaPanel({ data }: { data: HealthDto }) {
  const sym = data.currencySymbol;
  return (
    <div className="lg:border-l lg:border-border lg:pl-6">
      <PanelLabel>Income vs. Avg</PanelLabel>
      {data.incomeDeltaPercent === null ? (
        <PanelEmpty>Needs 6 months of income history</PanelEmpty>
      ) : (
        <>
          <Numeric className={`text-2xl font-bold ${incomeDeltaClass(data.incomeDeltaPercent)}`}>
            {data.incomeDeltaPercent >= 0 ? '+' : ''}{(data.incomeDeltaPercent * 100).toFixed(1)}%
          </Numeric>
          {data.currentMonthIncome != null && data.rollingAverageIncome != null && (
            <div className="mt-1 text-xs text-muted-foreground">
              <Numeric>{sym} {data.currentMonthIncome.toFixed(0)}</Numeric>
              {' vs '}
              <Numeric>{sym} {data.rollingAverageIncome.toFixed(0)}</Numeric>
              {' avg'}
            </div>
          )}
        </>
      )}
    </div>
  );
}

function BurnRatePanel({ data }: { data: HealthDto }) {
  const sym = data.currencySymbol;
  return (
    <div className="lg:border-l lg:border-border lg:pl-6">
      <PanelLabel>Budget Burn Rate</PanelLabel>
      {data.budgetBurnRate === null ? (
        <PanelEmpty>No active category budgets</PanelEmpty>
      ) : (
        <>
          <Numeric className={`text-2xl font-bold ${burnRateClass(data.budgetBurnRate)}`}>
            {(data.budgetBurnRate * 100).toFixed(1)}%
          </Numeric>
          {data.budgetSpentMtd != null && data.budgetTotalLimit != null && (
            <div className="mt-1 text-xs text-muted-foreground">
              <Numeric>{sym} {data.budgetSpentMtd.toFixed(0)}</Numeric>
              {' / '}
              <Numeric>{sym} {data.budgetTotalLimit.toFixed(0)}</Numeric>
              {' spent'}
            </div>
          )}
        </>
      )}
    </div>
  );
}
