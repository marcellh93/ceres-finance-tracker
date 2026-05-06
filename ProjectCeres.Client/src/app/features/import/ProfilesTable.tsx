import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { ProfileRowMenu } from './ProfileRowMenu';
import type { ImportProfileListItemDto } from './import-api';

type Props = {
  rows: ImportProfileListItemDto[];
  /** Called after a successful archive/reactivate so the parent layout can refetch. */
  onChanged: () => void;
};

export function ProfilesTable({ rows, onChanged }: Props) {
  if (rows.length === 0) {
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic">
        No profiles.
      </div>
    );
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>Name</TableHead>
          <TableHead className="w-24">Format</TableHead>
          <TableHead className="w-32">Status</TableHead>
          <TableHead className="w-12" />
        </TableRow>
      </TableHeader>
      <TableBody>
        {rows.map((row) => {
          const archived = row.deletedAt !== null;
          return (
            <TableRow
              key={row.id}
              className={archived ? 'opacity-60' : undefined}
            >
              <TableCell className="text-sm">{row.name}</TableCell>
              <TableCell className="text-sm">
                <Badge variant="secondary">{row.format}</Badge>
              </TableCell>
              <TableCell>
                {archived ? (
                  <Badge variant="secondary">
                    Archived · purges in {row.daysUntilPurge}d
                  </Badge>
                ) : null}
              </TableCell>
              <TableCell>
                <ProfileRowMenu profile={row} onChanged={onChanged} />
              </TableCell>
            </TableRow>
          );
        })}
      </TableBody>
    </Table>
  );
}
