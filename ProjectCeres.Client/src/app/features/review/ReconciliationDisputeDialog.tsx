import { useState } from 'react';
import { toast } from 'sonner';
import {
  AlertDialog, AlertDialogContent, AlertDialogHeader, AlertDialogTitle,
  AlertDialogDescription, AlertDialogFooter, AlertDialogCancel, AlertDialogAction,
} from '@/components/ui/alert-dialog';
import { RECONCILIATION_REVIEW_DISPUTE_URL } from './review-api';
import { apiFetch } from '../../lib/api-client';

type Props = {
  open: boolean;
  stagedId: string;
  onOpenChange: (next: boolean) => void;
  onDisputed: () => void;
};

export function ReconciliationDisputeDialog({ open, stagedId, onOpenChange, onDisputed }: Props) {
  const [submitting, setSubmitting] = useState(false);

  async function handleDispute() {
    if (submitting) return;
    setSubmitting(true);
    try {
      const res = await apiFetch(RECONCILIATION_REVIEW_DISPUTE_URL(stagedId), { method: 'POST' });
      if (res.ok) {
        toast.success('Disputed. Original un-cleared and a new transaction added.');
        onDisputed();
        onOpenChange(false);
      } else if (res.status === 404) {
        toast.error('That row no longer exists. Refreshing.');
        onDisputed();
        onOpenChange(false);
      } else {
        toast.error("Couldn't dispute. Try again.");
      }
    } catch {
      toast.error("Couldn't dispute. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Dispute this match?</AlertDialogTitle>
          <AlertDialogDescription>
            The matched transaction will be marked as not cleared, and this CSV row will be inserted as
            a new transaction marked "Needs review." You can fix mistakes from the Movements page.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction onClick={handleDispute} disabled={submitting}>
            {submitting ? 'Disputing…' : 'Dispute match'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
