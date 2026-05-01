import { useEffect, useRef } from 'react';
import { AccountBalancesChart } from '../features/dashboard/AccountBalancesChart';
import { CashFlowChart } from '../features/dashboard/CashFlowChart';
import { CategoryBudgetsCard } from '../features/dashboard/CategoryBudgetsCard';
import { FinancialHealthCard } from '../features/dashboard/FinancialHealthCard';
import { GoalBudgetsCard } from '../features/dashboard/GoalBudgetsCard';
import { IncomeExpenseChart } from '../features/dashboard/IncomeExpenseChart';
import { KpiStrip } from '../features/dashboard/KpiStrip';
import { NetWorthChart } from '../features/dashboard/NetWorthChart';
import { SpendingByCategoryChart } from '../features/dashboard/SpendingByCategoryChart';

export function Dashboard() {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, []);

  return (
    <div className="mx-auto max-w-7xl space-y-6">
      <h1
        ref={headingRef}
        tabIndex={-1}
        className="text-3xl font-semibold outline-none"
      >
        Dashboard
      </h1>

      <FinancialHealthCard />
      <KpiStrip />

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <CategoryBudgetsCard />
        <GoalBudgetsCard />
      </div>

      <NetWorthChart />

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <IncomeExpenseChart />
        <SpendingByCategoryChart />
        <AccountBalancesChart />
        <CashFlowChart />
      </div>
    </div>
  );
}
