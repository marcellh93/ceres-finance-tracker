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
import { ACCOUNT_ARCHIVE_URL, ACCOUNT_REACTIVATE_URL, type AccountListItemDto } from './accounts-api';

const ARCHIVE_COPY_EMPTY =
  "This account has no transactions. It will be hidden from the active list and pickers; you can find it again with the Include archived toggle. Safe to archive.";

const ARCHIVE_COPY_NON_EMPTY =
  "This account will be hidden from the active list and pickers. Existing transactions stay attached to it, and the balance still counts toward your net worth and reports. You can find archived accounts with the toggle.";

type Props = {
  account: AccountListItemDto;
  /** Called after a successful archive so the parent layout can refetch. */
  onChanged: () => void;
};

export function AccountRowMenu({ account, onChanged }: Props) {
  const navigate = useNavigate();
  const [confirmOpen, setConfirmOpen] = useState(false);

  async function handleArchive() {
    setConfirmOpen(false);
    try {
      const response = await fetch(ACCOUNT_ARCHIVE_URL(account.id), { method: 'PATCH' });
      if (response.ok) {
        toast.success('Archived.');
        onChanged();
        return;
      }
      toast.error("Couldn't archive. Try again.");
    } catch {
      toast.error("Couldn't archive. Try again.");
    }
  }

  async function handleReactivate() {
    try {
      const response = await fetch(ACCOUNT_REACTIVATE_URL(account.id), { method: 'PATCH' });
      if (response.ok) {
        toast.success('Reactivated.');
        onChanged();
        return;
      }
      toast.error("Couldn't reactivate. Try again.");
    } catch {
      toast.error("Couldn't reactivate. Try again.");
    }
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
          {account.isActive ? (
            <DropdownMenuItem onClick={() => navigate(`/accounts/${account.id}/edit`)}>
              Edit
            </DropdownMenuItem>
          ) : null}
          <DropdownMenuItem onClick={() => navigate(`/accounts/${account.id}/ledger`)}>
            View ledger
          </DropdownMenuItem>
          {account.isActive ? (
            <DropdownMenuItem onClick={() => setConfirmOpen(true)}>
              Archive…
            </DropdownMenuItem>
          ) : (
            <DropdownMenuItem onClick={handleReactivate}>
              Reactivate
            </DropdownMenuItem>
          )}
        </DropdownMenuContent>
      </DropdownMenu>

      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Archive '{account.name}'?</AlertDialogTitle>
            <AlertDialogDescription>
              {account.hasTransactions ? ARCHIVE_COPY_NON_EMPTY : ARCHIVE_COPY_EMPTY}
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
