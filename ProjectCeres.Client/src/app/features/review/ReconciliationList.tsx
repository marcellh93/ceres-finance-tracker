import { useState } from 'react';
import { Check } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { RECONCILIATION_REVIEW_PENDING_URL, type StagedTransactionDto } from './review-api';
import { ReconciliationCard } from './ReconciliationCard';
import { ReconciliationConfirmAllDialog } from './ReconciliationConfirmAllDialog';

type Props = {
  /** Called after any successful mutation so the parent count provider can refresh. */
  onChanged: () => void;
};

export function ReconciliationList({ onChanged }: Props) {
  const list = useApi<StagedTransactionDto[]>(RECONCILIATION_REVIEW_PENDING_URL);
  const [confirmAllOpen, setConfirmAllOpen] = useState(false);
  const [capturedCount, setCapturedCount] = useState(0);

  function handleChange() {
    list.refetch();
    onChanged();
  }

  if (list.loading) {
    return (
      <div className="flex flex-col gap-3">
        {Array.from({ length: 5 }).map((_, i) => (
          <Skeleton key={i} className="h-20 w-full" data-testid="reconciliation-skeleton" />
        ))}
      </div>
    );
  }

  if (list.error) {
    return <CardError section="Reconciliations" onRetry={list.refetch} />;
  }

  const rows = list.data ?? [];

  if (rows.length === 0) {
    return (
      <div className="py-12 text-center">
        <p className="text-base font-medium">No reconciliations to review.</p>
        <p className="mt-1 text-sm text-muted-foreground">
          The importer hasn't auto-matched any rows that need your confirmation.
        </p>
      </div>
    );
  }

  return (
    <>
      <div className="mb-4 flex items-start justify-between gap-4">
        <p className="text-sm text-muted-foreground">
          These rows were automatically matched to existing transactions during import.
          Confirm correct matches or dispute incorrect ones.
        </p>
        <Button
          variant="outline"
          onClick={() => {
            setCapturedCount(rows.length);
            setConfirmAllOpen(true);
          }}
        >
          <Check className="h-4 w-4" aria-hidden="true" />
          Confirm all
        </Button>
      </div>
      <div className="flex flex-col gap-3">
        {rows.map((staged) => (
          <ReconciliationCard key={staged.id} staged={staged} onChanged={handleChange} />
        ))}
      </div>
      <ReconciliationConfirmAllDialog
        open={confirmAllOpen}
        count={capturedCount}
        onOpenChange={setConfirmAllOpen}
        onConfirmed={handleChange}
      />
    </>
  );
}
