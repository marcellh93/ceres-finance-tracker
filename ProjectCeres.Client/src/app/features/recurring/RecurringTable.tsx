import { Badge } from '@/components/ui/badge';
import { classifyStatus, type RecurringTransactionListItemDto } from './reminder-status';
import { RecurringRowMenu } from './RecurringRowMenu';

const FREQUENCY_LABELS: Record<string, string> = {
  Weekly: 'Weekly', Biweekly: 'Biweekly', Monthly: 'Monthly', Annual: 'Annual',
};

const STATUS_LABELS: Record<string, string> = {
  overdue: 'Overdue', dueToday: 'Due today', manual: 'Manual', archived: 'Archived',
};

const STATUS_VARIANTS: Record<string, 'destructive' | 'default' | 'secondary' | 'outline'> = {
  overdue: 'destructive', dueToday: 'default', manual: 'secondary', archived: 'secondary',
};

type Props = {
  rows: RecurringTransactionListItemDto[];
  today: string;
  onChanged: () => void;
};

export function RecurringTable({ rows, today, onChanged }: Props) {
  return (
    <table className="w-full text-sm">
      <thead>
        <tr className="border-b text-muted-foreground text-xs uppercase tracking-wide">
          <th className="py-2 text-left font-medium">Name</th>
          <th className="w-32 py-2 text-left font-medium">Account</th>
          <th className="w-32 py-2 text-left font-medium">Category</th>
          <th className="w-24 py-2 text-left font-medium">Frequency</th>
          <th className="w-24 py-2 text-right font-medium tabular-nums">Est. amount</th>
          <th className="w-32 py-2 text-left font-medium">Next due</th>
          <th className="w-12 py-2" />
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <RecurringRow key={row.id} row={row} today={today} onChanged={onChanged} />
        ))}
      </tbody>
    </table>
  );
}

function RecurringRow({
  row, today, onChanged,
}: { row: RecurringTransactionListItemDto; today: string; onChanged: () => void }) {
  const statuses = classifyStatus(row, today);
  const archived = !row.isActive;

  return (
    <tr
      aria-label={row.name}
      className={`border-b last:border-0 ${archived ? 'opacity-60' : ''}`}
    >
      <td className="py-2 pr-4">
        <div className="flex items-center gap-2 flex-wrap">
          <span>{row.name}</span>
          {statuses.map((s) => (
            <Badge key={s} variant={STATUS_VARIANTS[s] ?? 'secondary'}>
              {STATUS_LABELS[s] ?? s}
            </Badge>
          ))}
        </div>
      </td>
      <td className="w-32 py-2 pr-4 truncate">{row.accountName}</td>
      <td className="w-32 py-2 pr-4 truncate" title={row.categoryName}>{row.categoryName}</td>
      <td className="w-24 py-2 pr-4">{FREQUENCY_LABELS[row.frequency] ?? row.frequency}</td>
      <td className="w-24 py-2 pr-4 text-right tabular-nums">
        {row.estimatedAmount == null
          ? '—'
          : `${row.currencySymbol}${row.estimatedAmount.toLocaleString()}`}
      </td>
      <td className="w-32 py-2 pr-4">{row.nextDueDate}</td>
      <td className="w-12 py-2 text-center">
        <RecurringRowMenu reminder={row} onChanged={onChanged} />
      </td>
    </tr>
  );
}
