import { useEffect, useRef } from 'react';
import { CategoryBudgetsCard } from '../features/dashboard/CategoryBudgetsCard';
import { FinancialHealthCard } from '../features/dashboard/FinancialHealthCard';
import { GoalBudgetsCard } from '../features/dashboard/GoalBudgetsCard';
import { KpiStrip } from '../features/dashboard/KpiStrip';

export function Dashboard() {
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, []);

  return (
    <div className="space-y-6">
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
    </div>
  );
}
