import { useState } from 'react';
import { Link as LinkIcon, Plus } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { TRANSFER_REVIEW_DISMISS_URL, type StagedTransferDto } from './review-api';
import { TransferActionDialog } from './TransferActionDialog';

// Minimal account shape needed by the dialog.
export type AccountOption = {
  id: string;
  name: string;
  isActive: boolean;
  currencyCode: string;
};

type Props = {
  staged: StagedTransferDto;
  accounts: AccountOption[];
  onChanged: () => void;
};

export function TransferCard({ staged, accounts, onChanged }: Props) {
  const [dismissing, setDismissing] = useState(false);
  const [linkOpen, setLinkOpen] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);

  async function handleDismiss() {
    if (dismissing) return;
    setDismissing(true);
    try {
      const res = await fetch(TRANSFER_REVIEW_DISMISS_URL(staged.id), { method: 'POST' });
      if (res.ok) {
        toast.success('Imported as a plain transaction.');
        onChanged();
      } else if (res.status === 404) {
        toast.error('That row no longer exists. Refreshing.');
        onChanged();
      } else {
        toast.error("Couldn't dismiss. Try again.");
      }
    } catch {
      toast.error("Couldn't dismiss. Try again.");
    } finally {
      setDismissing(false);
    }
  }

  const importedAtLabel = new Date(staged.importedAt).toLocaleString();
  const amountClass = staged.rawAmount < 0 ? 'text-rose-600' : 'text-emerald-700';
  const candidateAmountClass =
    (staged.candidateTransactionAmount ?? 0) < 0 ? 'text-rose-600' : 'text-emerald-700';

  const showCandidate =
    staged.candidateTransactionId !== null &&
    staged.candidateTransactionDate !== null &&
    staged.candidateTransactionAmount !== null;

  return (
    <>
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
        {showCandidate && (
          <div className="mt-2 inline-block rounded bg-sky-50 px-2 py-1 text-sm text-sky-800">
            Possible match: {staged.candidateTransactionDate}{' '}
            <span className={`tabular-nums ${candidateAmountClass}`}>
              {staged.accountCurrencySymbol}
              {(staged.candidateTransactionAmount as number).toFixed(2)}
            </span>
            {staged.candidateTransactionDescription
              ? ` ${staged.candidateTransactionDescription}`
              : ''}
          </div>
        )}
        <div className="mt-3 flex items-center justify-end gap-2">
          {staged.candidateTransactionId && (
            <Button onClick={() => setLinkOpen(true)} aria-label="Link to existing">
              <LinkIcon className="h-4 w-4" aria-hidden="true" />
              Link to existing
            </Button>
          )}
          <Button variant="outline" onClick={() => setCreateOpen(true)} aria-label="Create transfer">
            <Plus className="h-4 w-4" aria-hidden="true" />
            Create transfer
          </Button>
          <Button
            variant="outline"
            className="text-destructive"
            onClick={handleDismiss}
            disabled={dismissing}
          >
            {dismissing ? 'Dismissing…' : 'Dismiss'}
          </Button>
        </div>
      </div>

      <TransferActionDialog
        open={linkOpen}
        mode="link"
        stagedId={staged.id}
        ownAccountId={staged.accountId}
        ownAccountCurrencyCode={staged.accountCurrencyCode}
        accounts={accounts}
        onOpenChange={setLinkOpen}
        onActioned={onChanged}
      />
      <TransferActionDialog
        open={createOpen}
        mode="create"
        stagedId={staged.id}
        ownAccountId={staged.accountId}
        ownAccountCurrencyCode={staged.accountCurrencyCode}
        accounts={accounts}
        onOpenChange={setCreateOpen}
        onActioned={onChanged}
      />
    </>
  );
}
