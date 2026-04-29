import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Numeric } from '@/components/Numeric';
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
          <div className="grid grid-cols-1 lg:grid-cols-4 gap-6">
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
      <Row label="Liquid" value={`${sym} ${liquid.toFixed(2)}`} />
      {hasImminent && (
        <Row label="Bills due (7 days)" value={`−${sym} ${(data.imminentBills ?? 0).toFixed(2)}`} />
      )}
      <div className="border-t border-border pt-1 flex items-baseline justify-between">
        <span className="text-[10px] uppercase tracking-wider text-muted-foreground">Available today</span>
        <Numeric className={`text-base font-bold ${availableTodayClass(data.availableToday!)}`}>
          {sym} {data.availableToday!.toFixed(2)}
        </Numeric>
      </div>
      {hasLater && (
        <Row label="Bills later this month" value={`−${sym} ${(data.laterBills ?? 0).toFixed(2)}`} />
      )}
      {hasReserve && (
        <Row label="Budget reserved" value={`−${sym} ${(data.budgetReserve ?? 0).toFixed(2)}`} />
      )}
      {showSafeToSpend && (
        <div className="border-t border-border pt-1 flex items-baseline justify-between">
          <span className="text-[10px] uppercase tracking-wider text-muted-foreground">Safe to spend</span>
          <Numeric className={`text-sm font-semibold ${safeToSpendClass(data.safeToSpend!)}`}>
            {sym} {data.safeToSpend!.toFixed(2)}
          </Numeric>
        </div>
      )}
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between text-[11px] text-muted-foreground">
      <span>{label}</span>
      <Numeric className="text-[11px]">{value}</Numeric>
    </div>
  );
}

function RunwayPanel({ data }: { data: HealthDto }) {
  return (
    <div>
      <PanelLabel>Runway</PanelLabel>
      {data.runwayMonths === null ? (
        <PanelEmpty>Needs 6 months of expense history</PanelEmpty>
      ) : (
        <Numeric className={`text-lg font-bold ${runwayClass(data.runwayMonths)}`}>
          {data.runwayMonths.toFixed(1)} mo
        </Numeric>
      )}
    </div>
  );
}

function IncomeDeltaPanel({ data }: { data: HealthDto }) {
  return (
    <div>
      <PanelLabel>Income vs. Avg</PanelLabel>
      {data.incomeDeltaPercent === null ? (
        <PanelEmpty>Needs 6 months of income history</PanelEmpty>
      ) : (
        <Numeric className={`text-lg font-bold ${incomeDeltaClass(data.incomeDeltaPercent)}`}>
          {data.incomeDeltaPercent >= 0 ? '+' : ''}{(data.incomeDeltaPercent * 100).toFixed(1)}%
        </Numeric>
      )}
    </div>
  );
}

function BurnRatePanel({ data }: { data: HealthDto }) {
  return (
    <div>
      <PanelLabel>Budget Burn Rate</PanelLabel>
      {data.budgetBurnRate === null ? (
        <PanelEmpty>No active category budgets</PanelEmpty>
      ) : (
        <Numeric className={`text-lg font-bold ${burnRateClass(data.budgetBurnRate)}`}>
          {(data.budgetBurnRate * 100).toFixed(1)}%
        </Numeric>
      )}
    </div>
  );
}
