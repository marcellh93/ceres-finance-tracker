import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'sonner';
import { MoreHorizontal } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  AlertDialog, AlertDialogContent, AlertDialogHeader, AlertDialogTitle,
  AlertDialogDescription, AlertDialogFooter, AlertDialogCancel, AlertDialogAction,
} from '@/components/ui/alert-dialog';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { RECURRING_ARCHIVE_URL, RECURRING_REACTIVATE_URL } from './recurring-api';
import { RecurringConfirmDialog } from './RecurringConfirmDialog';
import { RecurringDismissDialog } from './RecurringDismissDialog';
import type { RecurringTransactionListItemDto } from './reminder-status';

type Props = {
  reminder: RecurringTransactionListItemDto;
  onChanged: () => void;
};

export function RecurringRowMenu({ reminder, onChanged }: Props) {
  const navigate = useNavigate();
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [dismissOpen, setDismissOpen] = useState(false);
  const [archiveOpen, setArchiveOpen] = useState(false);

  async function handleReactivate() {
    try {
      const res = await fetch(RECURRING_REACTIVATE_URL(reminder.id), { method: 'PATCH' });
      if (res.ok) { toast.success('Reactivated.'); onChanged(); return; }
      toast.error("Couldn't reactivate. Try again.");
    } catch {
      toast.error("Couldn't reactivate. Try again.");
    }
  }

  async function handleArchive() {
    setArchiveOpen(false);
    try {
      const res = await fetch(RECURRING_ARCHIVE_URL(reminder.id), { method: 'PATCH' });
      if (res.ok) { toast.success('Archived.'); onChanged(); return; }
      toast.error("Couldn't archive. Try again.");
    } catch {
      toast.error("Couldn't archive. Try again.");
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
          {reminder.isActive ? (
            <>
              <DropdownMenuItem onClick={() => setConfirmOpen(true)}>Confirm…</DropdownMenuItem>
              <DropdownMenuItem onClick={() => navigate(`/recurring/${reminder.id}/edit`)}>Edit</DropdownMenuItem>
              <DropdownMenuItem onClick={() => setDismissOpen(true)}>Dismiss…</DropdownMenuItem>
              <DropdownMenuItem onClick={() => setArchiveOpen(true)}>Archive…</DropdownMenuItem>
            </>
          ) : (
            <DropdownMenuItem onClick={handleReactivate}>Reactivate</DropdownMenuItem>
          )}
        </DropdownMenuContent>
      </DropdownMenu>

      <RecurringConfirmDialog
        open={confirmOpen} reminder={reminder} onChanged={onChanged} onOpenChange={setConfirmOpen}
      />

      <RecurringDismissDialog
        open={dismissOpen} reminder={reminder} onChanged={onChanged} onOpenChange={setDismissOpen}
      />

      <AlertDialog open={archiveOpen} onOpenChange={setArchiveOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Archive &apos;{reminder.name}&apos;?</AlertDialogTitle>
            <AlertDialogDescription>
              This reminder will stop appearing in the active list and will not advance any further.
              Existing transactions stay attached to your account history. You can reactivate it
              later from the archived list.
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
