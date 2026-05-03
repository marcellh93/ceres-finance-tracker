import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
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
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Name</TableHead>
          <TableHead className="w-32">Account</TableHead>
          <TableHead className="w-32">Category</TableHead>
          <TableHead className="w-24">Frequency</TableHead>
          <TableHead className="w-28 text-right">Est. amount</TableHead>
          <TableHead className="w-32">Next due</TableHead>
          <TableHead className="w-12" />
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map((row) => (
          <RecurringRow key={row.id} row={row} today={today} onChanged={onChanged} />
        ))}
      </TableBody>
    </Table>
  );
}

function RecurringRow({
  row, today, onChanged,
}: { row: RecurringTransactionListItemDto; today: string; onChanged: () => void }) {
  const statuses = classifyStatus(row, today);
  const archived = !row.isActive;

  return (
    <TableRow className={archived ? 'opacity-60' : undefined} aria-label={row.name}>
      <TableCell className="text-sm">
        <div className="flex items-center gap-2 min-w-0">
          <span className="truncate">{row.name}</span>
          {statuses.map((s) => (
            <Badge key={s} variant={STATUS_VARIANTS[s] ?? 'secondary'} className="shrink-0">
              {STATUS_LABELS[s] ?? s}
            </Badge>
          ))}
        </div>
      </TableCell>
      <TableCell className="text-sm text-muted-foreground">{row.accountName}</TableCell>
      <TableCell className="text-sm text-muted-foreground" title={row.categoryName}>{row.categoryName}</TableCell>
      <TableCell className="text-sm">{FREQUENCY_LABELS[row.frequency] ?? row.frequency}</TableCell>
      <TableCell className="text-right tabular-nums text-sm">
        {row.estimatedAmount == null
          ? '—'
          : `${row.currencySymbol}${row.estimatedAmount.toLocaleString()}`}
      </TableCell>
      <TableCell className="text-sm">{row.nextDueDate}</TableCell>
      <TableCell>
        <RecurringRowMenu reminder={row} onChanged={onChanged} />
      </TableCell>
    </TableRow>
  );
}
