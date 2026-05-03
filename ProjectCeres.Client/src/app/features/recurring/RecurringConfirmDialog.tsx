import { useState, useEffect } from 'react';
import { toast } from 'sonner';
import {
  AlertDialog, AlertDialogContent, AlertDialogHeader, AlertDialogTitle,
  AlertDialogDescription, AlertDialogFooter, AlertDialogCancel, AlertDialogAction,
} from '@/components/ui/alert-dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { DatePickerField } from '../../../components/DatePickerField';
import { RECURRING_CONFIRM_URL } from './recurring-api';
import type { RecurringTransactionListItemDto } from './reminder-status';

type Props = {
  open: boolean;
  reminder: RecurringTransactionListItemDto;
  onChanged: () => void;
  onOpenChange: (open: boolean) => void;
};

export function RecurringConfirmDialog({ open, reminder, onChanged, onOpenChange }: Props) {
  const [date, setDate] = useState(reminder.nextDueDate);
  const [amount, setAmount] = useState(reminder.estimatedAmount?.toString() ?? '');
  const [description, setDescription] = useState('');
  const [nextDueDate, setNextDueDate] = useState('');
  const [inlineError, setInlineError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const isManual = reminder.reminderBehaviour === 'ManualDate';

  useEffect(() => {
    if (open) {
      setDate(reminder.nextDueDate);
      setAmount(reminder.estimatedAmount?.toString() ?? '');
      setDescription('');
      setNextDueDate('');
      setInlineError('');
    }
  }, [open, reminder]);

  async function handleConfirm() {
    setSubmitting(true);
    setInlineError('');
    try {
      const res = await fetch(RECURRING_CONFIRM_URL(reminder.id), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          date,
          amount: Number(amount),
          description: description.trim() || null,
          nextDueDate: isManual ? nextDueDate : null,
        }),
      });
      if (res.status === 201) {
        toast.success(`Recorded ${reminder.currencySymbol}${amount} on ${date}.`);
        onOpenChange(false);
        onChanged();
        return;
      }
      const body = await res.json().catch(() => ({})) as { error?: { code?: string } };
      if (body.error?.code === 'DATE_BEFORE_OPENING_BALANCE') {
        setInlineError("That date is before this account's opening balance.");
        return;
      }
      toast.error("Couldn't record. Try again.");
    } catch {
      toast.error("Couldn't record. Try again.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Confirm &apos;{reminder.name}&apos;?</AlertDialogTitle>
          <AlertDialogDescription>Record this reminder as a transaction.</AlertDialogDescription>
        </AlertDialogHeader>
        <div className="space-y-3">
          <div className="space-y-1.5">
            <Label htmlFor="confirm-date">Date *</Label>
            <DatePickerField
              id="confirm-date"
              value={date || null}
              onChange={(v) => setDate(v ?? '')}
              hideClear
            />
            {inlineError && <p className="text-sm text-destructive">{inlineError}</p>}
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="confirm-amount">Amount *</Label>
            <Input id="confirm-amount" type="number" min={0} step="0.01"
              value={amount} onChange={(e) => setAmount(e.target.value)} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="confirm-desc">Description</Label>
            <Input id="confirm-desc" placeholder={reminder.name}
              value={description} onChange={(e) => setDescription(e.target.value)} />
          </div>
          {isManual && (
            <div className="space-y-1.5">
              <Label htmlFor="confirm-next-due">Next due date *</Label>
              <DatePickerField
                id="confirm-next-due"
                value={nextDueDate || null}
                onChange={(v) => setNextDueDate(v ?? '')}
                hideClear
              />
            </div>
          )}
        </div>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction onClick={handleConfirm} disabled={submitting || !amount || (isManual && !nextDueDate)}>
            {submitting ? 'Confirming…' : 'Confirm'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
