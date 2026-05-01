import { useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { toast } from 'sonner';
import { CheckCheck, Download } from 'lucide-react';
import { Button, buttonVariants } from '@/components/ui/button';
import { cn } from '@/lib/utils';
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
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';
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
  const currency = searchParams.get('currency');

  const noDateFilter = !from && !to;
  const noun = totalCount === 1 ? 'movement' : 'movements';

  async function handleConfirm() {
    const body: BulkClearedRequest = {
      from,
      to,
      accountId: accountId ?? null,
      type: type ?? null,
      currency: currency ?? null,
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

  // The Export CSV button renders as a real <a download> so the click is a
  // first-class user gesture — the browser fires its full native download
  // chrome, including the macOS flying-file animation from the button
  // toward the Downloads icon. Programmatic .click() on a synthesised
  // anchor doesn't reliably fire that animation in Safari/Chrome; rendering
  // an actual visible <a> does.
  const exportHref = MOVEMENTS_EXPORT_CSV_URL(searchParams.toString());
  function handleExportClick() {
    toast.success('Exporting movements…');
  }

  return (
    <div className="flex items-center gap-2">
      <TooltipProvider delay={0}>
        <Tooltip>
          <TooltipTrigger
            render={
              <Button
                type="button"
                variant="outline"
                onClick={() => setOpen(true)}
                className="gap-2"
              >
                <CheckCheck className="h-4 w-4" />
                Mark visible cleared
              </Button>
            }
          />
          <TooltipContent>
            Marks every movement currently visible in the table as cleared (reconciled with your bank statement).
          </TooltipContent>
        </Tooltip>
      </TooltipProvider>
      {/*
        Plain <a> (NOT wrapped in base-ui Button — that hook merges click
        handlers via mergeProps which suppresses the browser's native
        download UX). The server returns Content-Disposition: attachment,
        so the browser will treat this as a download regardless of
        whether the <a> carries the `download` attribute. We deliberately
        omit `download` here: in some Chromium-based browsers (Arc),
        an explicit `download` attribute is treated as 'silent-save' and
        skips the genie animation that fires for browser-initiated
        downloads driven purely by Content-Disposition.
      */}
      <a
        href={exportHref}
        rel="noopener"
        onClick={handleExportClick}
        className={cn(buttonVariants({ variant: 'outline' }), 'gap-2 no-underline')}
      >
        <Download className="h-4 w-4" />
        Export CSV
      </a>


      <AlertDialog open={open} onOpenChange={setOpen}>
        {/*
          finalFocus={false} on the Popup tells base-ui's dialog primitive
          NOT to return focus to the trigger button on close. Without this,
          the returned focus is treated by the shadcn Tooltip as a hover
          (focus and hover share the same open trigger), so the tooltip
          re-appears and sits on screen until the user clicks somewhere
          else. With finalFocus={false}, focus falls back to <body>.
          Keyboard users who want to act again can Tab back to the button;
          the tooltip will appear correctly on that focus.
        */}
        <AlertDialogContent finalFocus={false}>
          <AlertDialogHeader>
            <AlertDialogTitle>
              Mark {totalCount} {noun} as cleared?
            </AlertDialogTitle>
            <AlertDialogDescription>
              {noDateFilter
                ? `You haven't filtered by date. This will mark every ${currency ? `${currency} ` : ''}movement in the table as cleared. This cannot be undone in bulk.`
                : 'Movements matching the active filter will be marked cleared. This cannot be undone in bulk.'}
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
