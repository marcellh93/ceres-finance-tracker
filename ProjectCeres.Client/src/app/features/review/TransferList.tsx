import { Skeleton } from '@/components/ui/skeleton';
import { CardError } from '../../components/CardError';
import { useApi } from '../../lib/use-api';
import { ACCOUNTS_URL, type AccountListItemDto } from '../accounts/accounts-api';
import { TRANSFER_REVIEW_PENDING_URL, type StagedTransferDto } from './review-api';
import { TransferCard, type AccountOption } from './TransferCard';

type Props = {
  /** Called after any successful mutation in a child card so the parent count provider can refresh. */
  onChanged: () => void;
};

export function TransferList({ onChanged }: Props) {
  const list = useApi<StagedTransferDto[]>(TRANSFER_REVIEW_PENDING_URL);
  // Independent fetch — we deliberately don't propagate this error to the list-level error UI.
  // If accounts fail to load, the picker dialog (Task 16) will degrade to its own empty state.
  const accountsApi = useApi<AccountListItemDto[]>(ACCOUNTS_URL);

  // Map AccountListItemDto (the wire shape) to AccountOption (the dialog's stable internal contract).
  // The wire DTO carries many extra fields (balance, hasTransactions, etc.) the picker doesn't need;
  // narrowing here keeps the picker decoupled from the full account API surface.
  const accounts: AccountOption[] = (accountsApi.data ?? []).map((a) => ({
    id: a.id,
    name: a.name,
    isActive: a.isActive,
    currencyCode: a.currencyCode,
  }));

  function handleChange() {
    list.refetch();
    onChanged();
  }

  if (list.loading) {
    return (
      <div className="flex flex-col gap-3">
        {Array.from({ length: 5 }).map((_, i) => (
          <Skeleton key={i} className="h-20 w-full" data-testid="transfer-skeleton" />
        ))}
      </div>
    );
  }

  if (list.error) {
    return <CardError section="Transfers" onRetry={list.refetch} />;
  }

  const rows = list.data ?? [];

  if (rows.length === 0) {
    return (
      <div className="py-12 text-center">
        <p className="text-base font-medium">No transfers to review.</p>
        <p className="mt-1 text-sm text-muted-foreground">
          The importer hasn't flagged any rows that look like transfers.
        </p>
      </div>
    );
  }

  return (
    <>
      <p className="mb-4 text-sm text-muted-foreground">
        These rows look like transfers between accounts. Resolve each one before they appear as plain transactions.
      </p>
      <div className="flex flex-col gap-3">
        {rows.map((staged) => (
          <TransferCard
            key={staged.id}
            staged={staged}
            accounts={accounts}
            onChanged={handleChange}
          />
        ))}
      </div>
    </>
  );
}
