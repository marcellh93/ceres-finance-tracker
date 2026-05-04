import { useState } from 'react';
import { Check, MoreHorizontal } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { RECONCILIATION_REVIEW_CONFIRM_URL, type StagedTransactionDto } from './review-api';

type Props = {
  staged: StagedTransactionDto;
  onChanged: () => void;
};

export function ReconciliationCard({ staged, onChanged }: Props) {
  const [confirming, setConfirming] = useState(false);

  async function handleConfirm() {
    if (confirming) return;
    setConfirming(true);
    try {
      const res = await fetch(RECONCILIATION_REVIEW_CONFIRM_URL(staged.id), { method: 'POST' });
      if (res.ok) {
        toast.success('Confirmed.');
        onChanged();
      } else if (res.status === 404) {
        toast.error('That row no longer exists. Refreshing.');
        onChanged();
      } else {
        toast.error("Couldn't confirm. Try again.");
      }
    } catch {
      toast.error("Couldn't confirm. Try again.");
    } finally {
      setConfirming(false);
    }
  }

  const importedAtLabel = new Date(staged.importedAt).toLocaleString();
  const amountClass = staged.rawAmount < 0 ? 'text-rose-600' : 'text-emerald-700';
  const matchedAmountClass =
    staged.matchedTransactionAmount < 0 ? 'text-rose-600' : 'text-emerald-700';

  return (
    <div className="rounded-md border border-border bg-card p-4 shadow-sm">
      <div className="text-xs text-muted-foreground">
        {staged.accountName} — imported {importedAtLabel}
      </div>
      <div className="mt-1 flex items-baseline gap-2 text-base font-semibold">
        <span>{staged.rawDate}</span>
        <span className={`tabular-nums ${amountClass}`}>
          {staged.accountCurrencySymbol}
          {staged.rawAmount.toFixed(2)}
        </span>
      </div>
      {staged.rawDescription && (
        <div className="mt-0.5 text-sm text-muted-foreground">{staged.rawDescription}</div>
      )}
      <div className="mt-2 inline-block rounded bg-sky-50 px-2 py-1 text-sm text-sky-800">
        Matched to: {staged.matchedTransactionDate}{' '}
        <span className={`tabular-nums ${matchedAmountClass}`}>
          {staged.accountCurrencySymbol}
          {staged.matchedTransactionAmount.toFixed(2)}
        </span>
        {staged.matchedTransactionDescription ? ` ${staged.matchedTransactionDescription}` : ''}
      </div>
      <div className="mt-3 flex items-center justify-end gap-2">
        <Button onClick={handleConfirm} disabled={confirming}>
          <Check className="h-4 w-4" aria-hidden="true" />
          {confirming ? 'Confirming…' : 'Confirm match'}
        </Button>
        <DropdownMenu>
          <DropdownMenuTrigger
            render={
              <Button variant="ghost" size="icon" aria-label="More actions">
                <MoreHorizontal className="h-4 w-4" />
              </Button>
            }
          />
          <DropdownMenuContent align="end">
            {/* TODO(Task 12): replace with a real Dispute item that opens ReconciliationDisputeDialog */}
            <DropdownMenuItem disabled>Dispute (wired in Task 12)</DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </div>
  );
}
