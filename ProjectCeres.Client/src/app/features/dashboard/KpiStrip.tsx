import { MtdCard } from './MtdCard';
import { NetWorthCard } from './NetWorthCard';
import { RemindersCard } from './RemindersCard';

export function KpiStrip() {
  return (
    <div className="grid grid-cols-1 sm:grid-cols-3 gap-6">
      <NetWorthCard />
      <MtdCard />
      <RemindersCard />
    </div>
  );
}
