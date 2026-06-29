import { useState, useEffect } from 'react';
import { toast } from 'sonner';
import {
  AlertDialog, AlertDialogContent, AlertDialogHeader, AlertDialogTitle,
  AlertDialogDescription, AlertDialogFooter, AlertDialogCancel, AlertDialogAction,
} from '@/components/ui/alert-dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { RECURRING_DISMISS_URL } from './recurring-api';
import type { RecurringTransactionListItemDto } from './reminder-status';

type Props = {
  open: boolean;
  reminder: RecurringTransactionListItemDto;
  onChanged: () => void;
  onOpenChange: (open: boolean) => void;
};

export function RecurringDismissDialog({ open, reminder, onChanged, onOpenChange }: Props) {
  const [nextDueDate, setNextDueDate] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const isManual = reminder.reminderBehaviour === 'ManualDate';

  // eslint-disable-next-line react-hooks/set-state-in-effect -- Why: dialog field reset on open event; clearing controlled input state when the dialog opens is an external-event-driven reset, not a cascading render.
  useEffect(() => { if (open) setNextDueDate(''); }, [open]);

  async function handleDismiss() {
    setSubmitting(true);
    try {
      const res = await fetch(RECURRING_DISMISS_URL(reminder.id), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ nextDueDate: isManual ? nextDueDate : null }),
      });
      if (res.ok) {
        toast.success('Dismissed. Next due date advanced.');
        onOpenChange(false);
        onChanged();
        return;
      }
      toast.error("Couldn't dismiss. Try again.");
    } catch {
      toast.error("Couldn't dismiss. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  const description = isManual
    ? 'No transaction will be recorded. Pick the next due date manually.'
    : 'No transaction will be recorded. The next due date advances to the following period.';

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Dismiss &apos;{reminder.name}&apos;?</AlertDialogTitle>
          <AlertDialogDescription>{description}</AlertDialogDescription>
        </AlertDialogHeader>
        {isManual && (
          <div className="space-y-1.5">
            <Label htmlFor="dismiss-next-due">Next due date *</Label>
            <Input id="dismiss-next-due" type="date"
              value={nextDueDate} onChange={(e) => setNextDueDate(e.target.value)} />
          </div>
        )}
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction
            onClick={handleDismiss}
            disabled={submitting || (isManual && !nextDueDate)}
          >
            {submitting ? 'Dismissing…' : 'Dismiss'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
