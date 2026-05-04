import { useState } from 'react';
import { toast } from 'sonner';
import {
  AlertDialog, AlertDialogContent, AlertDialogHeader, AlertDialogTitle,
  AlertDialogDescription, AlertDialogFooter, AlertDialogCancel, AlertDialogAction,
} from '@/components/ui/alert-dialog';
import { RECONCILIATION_REVIEW_CONFIRM_ALL_URL } from './review-api';

type Props = {
  open: boolean;
  count: number;
  onOpenChange: (next: boolean) => void;
  onConfirmed: () => void;
};

export function ReconciliationConfirmAllDialog({ open, count, onOpenChange, onConfirmed }: Props) {
  const [submitting, setSubmitting] = useState(false);

  async function handleConfirm() {
    if (submitting) return;
    setSubmitting(true);
    try {
      const res = await fetch(RECONCILIATION_REVIEW_CONFIRM_ALL_URL, { method: 'POST' });
      if (res.ok) {
        toast.success(`Confirmed ${count} matches.`);
        onConfirmed();
        onOpenChange(false);
      } else {
        toast.error("Couldn't confirm all. Try again.");
      }
    } catch {
      toast.error("Couldn't confirm all. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Confirm all {count} matches?</AlertDialogTitle>
          <AlertDialogDescription>
            This accepts every staged match in the list. You can still adjust individual transactions later
            from Movements, but confirming clears them from this queue all at once.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction onClick={handleConfirm} disabled={submitting}>
            {submitting ? 'Confirming…' : 'Confirm all'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
