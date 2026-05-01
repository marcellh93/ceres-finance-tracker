import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'sonner';
import { MoreHorizontal } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  CATEGORY_BUDGET_ARCHIVE_URL,
  CATEGORY_BUDGET_REACTIVATE_URL,
  GOAL_BUDGET_ARCHIVE_URL,
  GOAL_BUDGET_REACTIVATE_URL,
  type BudgetKind,
} from './budgets-api';

type Props = {
  budgetId: string;
  kind: BudgetKind;
  isActive: boolean;
  /** What kind of thing this is in plain English: "category budget" / "spending goal" / "savings goal". */
  noun: string;
  onChanged: () => void;
};

export function BudgetRowMenu({ budgetId, kind, isActive, noun, onChanged }: Props) {
  const navigate = useNavigate();
  const [confirmOpen, setConfirmOpen] = useState(false);

  const archiveUrl =
    kind === 'CategoryBudget'
      ? CATEGORY_BUDGET_ARCHIVE_URL(budgetId)
      : GOAL_BUDGET_ARCHIVE_URL(budgetId);

  const reactivateUrl =
    kind === 'CategoryBudget'
      ? CATEGORY_BUDGET_REACTIVATE_URL(budgetId)
      : GOAL_BUDGET_REACTIVATE_URL(budgetId);

  async function handleArchive() {
    const response = await fetch(archiveUrl, { method: 'PATCH' });
    if (response.status === 204) {
      toast.success(`${capitalize(noun)} archived.`);
      onChanged();
      return;
    }
    toast.error(`Couldn't archive the ${noun}.`);
  }

  async function handleReactivate() {
    const response = await fetch(reactivateUrl, { method: 'PATCH' });
    if (response.status === 204) {
      toast.success(`${capitalize(noun)} reactivated.`);
      onChanged();
      return;
    }
    if (response.status === 409) {
      const body = await response.json().catch(() => null);
      const message = body?.error?.message ?? `Couldn't reactivate the ${noun}.`;
      toast.error(message);
      return;
    }
    toast.error(`Couldn't reactivate the ${noun}.`);
  }

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger
          render={
            <Button variant="ghost" size="icon" aria-label="Row actions">
              <MoreHorizontal className="h-4 w-4" />
            </Button>
          }
        />
        <DropdownMenuContent align="end">
          <DropdownMenuItem onClick={() => navigate(`/budgets/${budgetId}/edit`)}>
            Edit
          </DropdownMenuItem>
          {isActive ? (
            <DropdownMenuItem onClick={() => setConfirmOpen(true)}>
              Archive
            </DropdownMenuItem>
          ) : (
            <DropdownMenuItem onClick={handleReactivate}>
              Reactivate
            </DropdownMenuItem>
          )}
        </DropdownMenuContent>
      </DropdownMenu>

      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent finalFocus={false}>
          <AlertDialogHeader>
            <AlertDialogTitle>Archive this {noun}?</AlertDialogTitle>
            <AlertDialogDescription>
              It will be hidden from the active list but historical data is preserved. You can
              reactivate it later.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={handleArchive}>Archive</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}

function capitalize(s: string): string {
  return s.length === 0 ? s : s[0].toUpperCase() + s.slice(1);
}
