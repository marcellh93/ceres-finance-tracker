import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { toast } from 'sonner';
import { CheckCheck, Download } from 'lucide-react';
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
  MOVEMENTS_BULK_CLEARED_URL,
  MOVEMENTS_EXPORT_CSV_URL,
  type BulkClearedRequest,
  type BulkClearedResponse,
} from './movements-api';

type MovementsBulkActionsProps = {
  totalCount: number;
  onAfterBulk: () => void;
};

type SpaType = BulkClearedRequest['type'];

function parseType(value: string | null): SpaType {
  if (value === 'transaction' || value === 'transfer' || value === 'liabilitypayment') {
    return value;
  }
  return null;
}

export function MovementsBulkActions({ totalCount, onAfterBulk }: MovementsBulkActionsProps) {
  const [searchParams] = useSearchParams();
  const [open, setOpen] = useState(false);

  const from = searchParams.get('from') ?? '';
  const to = searchParams.get('to') ?? '';
  const accountId = searchParams.get('accountId');
  const type = parseType(searchParams.get('type'));

  const bulkDisabled = !from && !to;
  const noun = totalCount === 1 ? 'movement' : 'movements';

  async function handleConfirm() {
    const body: BulkClearedRequest = {
      from,
      to,
      accountId: accountId ?? null,
      type: type ?? null,
    };
    const response = await fetch(MOVEMENTS_BULK_CLEARED_URL, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    if (response.status === 200) {
      const data = (await response.json()) as BulkClearedResponse;
      const count = data?.cleared ?? 0;
      toast.success(`${count} marked cleared.`);
      setOpen(false);
      onAfterBulk();
      return;
    }
    toast.error("Couldn't mark as cleared.");
  }

  function handleExport() {
    window.location.href = MOVEMENTS_EXPORT_CSV_URL(searchParams.toString());
  }

  return (
    <div className="flex items-center gap-2">
      <Button
        type="button"
        variant="outline"
        onClick={() => setOpen(true)}
        disabled={bulkDisabled}
        title={bulkDisabled ? 'Set a date range first to enable bulk actions' : undefined}
        className="gap-2"
      >
        <CheckCheck className="h-4 w-4" />
        Mark visible cleared
      </Button>
      <Button
        type="button"
        variant="outline"
        onClick={handleExport}
        className="gap-2"
      >
        <Download className="h-4 w-4" />
        Export CSV
      </Button>

      <AlertDialog open={open} onOpenChange={setOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              Mark {totalCount} {noun} as cleared?
            </AlertDialogTitle>
            <AlertDialogDescription>
              Movements matching the active filter will be marked cleared. This cannot be undone in bulk.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={handleConfirm}>Mark cleared</AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
