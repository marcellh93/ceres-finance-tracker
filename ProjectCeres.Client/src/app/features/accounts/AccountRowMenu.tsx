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
import { Label } from '@/components/ui/label';
import { Switch } from '@/components/ui/switch';
import { ACCOUNT_ARCHIVE_URL, ACCOUNT_REACTIVATE_URL, type AccountListItemDto } from './accounts-api';
import { apiFetch } from '../../lib/api-client';

const ARCHIVE_COPY_EMPTY =
  "This account has no transactions. It will be hidden from the active list and pickers; you can find it again with the Include archived toggle.";

const ARCHIVE_COPY_NON_EMPTY =
  "This account will be hidden from the active list and pickers. Existing transactions stay attached to it. By default the balance still counts toward your net worth and reports — toggle the option below to exclude it.";

type Props = {
  account: AccountListItemDto;
  /** Called after a successful archive so the parent layout can refetch. */
  onChanged: () => void;
};

export function AccountRowMenu({ account, onChanged }: Props) {
  const navigate = useNavigate();
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [excludeFromReports, setExcludeFromReports] = useState(false);

  function openConfirm() {
    setExcludeFromReports(false);
    setConfirmOpen(true);
  }

  async function handleArchive() {
    const exclude = excludeFromReports;
    setConfirmOpen(false);
    try {
      const response = await apiFetch(ACCOUNT_ARCHIVE_URL(account.id), {
        method: 'PATCH',
        body: { excludeFromReports: exclude },
      });
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
      const response = await apiFetch(ACCOUNT_REACTIVATE_URL(account.id), { method: 'PATCH' });
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
            <DropdownMenuItem onClick={openConfirm}>
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
          {account.hasTransactions ? (
            <div className="flex items-start gap-3 rounded-md border border-input bg-muted/40 px-3 py-3">
              <Switch
                id="excludeFromReports"
                checked={excludeFromReports}
                onCheckedChange={setExcludeFromReports}
              />
              <Label htmlFor="excludeFromReports" className="text-sm font-normal leading-tight cursor-pointer">
                Also exclude from net worth and reports
              </Label>
            </div>
          ) : null}
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={handleArchive}>Archive</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}
