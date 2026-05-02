import { Lock } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import { CategoryRowMenu } from './CategoryRowMenu';
import {
  isLockedCategory,
  type CategoryListItemDto,
} from './categories-api';

const SYSTEM_TOOLTIP =
  'Created by the system. These are required for imports and accounting and cannot be edited or archived.';

type Props = {
  rows: CategoryListItemDto[];
  /** Called after a successful archive so the parent layout can refetch. */
  onChanged: () => void;
};

export function CategoriesTable({ rows, onChanged }: Props) {
  if (rows.length === 0) {
    return (
      <div className="px-3 py-8 text-center text-sm text-muted-foreground italic">
        No categories.
      </div>
    );
  }

  return (
    <TooltipProvider delay={200}>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Name</TableHead>
            <TableHead className="w-32">Lifestyle</TableHead>
            <TableHead className="w-12" />
          </TableRow>
        </TableHeader>
        <TableBody>
          {rows.map((row) => {
            const locked = isLockedCategory(row);
            return (
              <TableRow
                key={row.id}
                className={!row.isActive ? 'opacity-60' : undefined}
              >
                <TableCell className="text-sm">
                  <div className="flex items-center gap-2">
                    {locked ? (
                      <Tooltip>
                        <TooltipTrigger
                          render={
                            <button
                              type="button"
                              aria-label="System category — locked"
                              className="text-muted-foreground/70 hover:text-muted-foreground transition-colors cursor-help"
                            >
                              <Lock className="h-3.5 w-3.5" aria-hidden="true" />
                            </button>
                          }
                        />
                        <TooltipContent className="max-w-xs">
                          {SYSTEM_TOOLTIP}
                        </TooltipContent>
                      </Tooltip>
                    ) : null}
                    <span className={locked ? 'text-muted-foreground' : undefined}>
                      {row.name}
                    </span>
                    {!row.isActive ? (
                      <Badge variant="secondary">Archived</Badge>
                    ) : null}
                  </div>
                </TableCell>
                <TableCell>
                  {row.lifestyleTag ? (
                    <Badge variant="secondary">{row.lifestyleTag}</Badge>
                  ) : null}
                </TableCell>
                <TableCell>
                  {locked ? null : (
                    <CategoryRowMenu
                      category={row}
                      onChanged={onChanged}
                    />
                  )}
                </TableCell>
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </TooltipProvider>
  );
}
