import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { cn } from '@/lib/utils';
import { AccountRowMenu } from './AccountRowMenu';
import type { AccountListItemDto } from './accounts-api';

type Props = {
  rows: AccountListItemDto[];
  /** Called after a successful archive so the parent layout can refetch. */
  onChanged: () => void;
};

export function AccountsTable({ rows, onChanged }: Props) {
  if (rows.length === 0) {
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic">
        No accounts.
      </div>
    );
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Name</TableHead>
          <TableHead className="w-32">Type</TableHead>
          <TableHead className="w-40 text-right">Balance</TableHead>
          <TableHead className="w-12" />
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map((row) => {
          const isLiability = row.accountTypeName === 'Liability';
          return (
            <TableRow
              key={row.id}
              className={!row.isActive ? 'opacity-60' : undefined}
            >
              <TableCell className="text-sm">
                <div className="flex items-center gap-2">
                  <span>{row.name}</span>
                  {!row.isActive ? (
                    <Badge variant="secondary">Archived</Badge>
                  ) : null}
                </div>
              </TableCell>
              <TableCell className="text-sm text-muted-foreground">
                {row.accountTypeName}
              </TableCell>
              <TableCell
                className={cn(
                  'text-right tabular-nums text-sm',
                  isLiability && 'text-destructive',
                )}
              >
                {formatBalance(row)}
              </TableCell>
              <TableCell>
                <AccountRowMenu account={row} onChanged={onChanged} />
              </TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}

function formatBalance(account: AccountListItemDto): string {
  const formatted = new Intl.NumberFormat(undefined, {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(Math.abs(account.balance));
  const sign = account.balance < 0 ? '-' : '';
  return `${sign}${account.currencySymbol}${formatted}`;
}
