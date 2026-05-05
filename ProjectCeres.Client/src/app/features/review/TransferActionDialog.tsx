import { useMemo, useState } from 'react';
import { Check, ChevronsUpDown } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  AlertDialog, AlertDialogContent, AlertDialogHeader, AlertDialogTitle,
  AlertDialogDescription, AlertDialogFooter, AlertDialogCancel, AlertDialogAction,
} from '@/components/ui/alert-dialog';
import {
  Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList,
} from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import {
  TRANSFER_REVIEW_LINK_URL, TRANSFER_REVIEW_CREATE_URL,
} from './review-api';
import type { AccountOption } from './TransferCard';

type Props = {
  open: boolean;
  mode: 'link' | 'create';
  stagedId: string;
  ownAccountId: string;
  ownAccountCurrencyCode: string;
  accounts: AccountOption[];
  onOpenChange: (next: boolean) => void;
  onActioned: () => void;
};

// The server's TryLinkToExistingAsync / TryCreateAsTransferAsync surface only three error codes:
//   NOT_FOUND, INVALID_ACCOUNT, VALIDATION_ERROR (which wraps any InvalidOperationException
//   thrown by TransferService.CreateAsync, including the same-currency check).
// The picker below pre-filters by currency, so a 422 here would be a defensive case the user
// should never hit. A generic "Couldn't link/create. Try again." toast is sufficient.

export function TransferActionDialog({
  open, mode, stagedId, ownAccountId, ownAccountCurrencyCode,
  accounts, onOpenChange, onActioned,
}: Props) {
  const [otherAccountId, setOtherAccountId] = useState<string | null>(null);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  const eligible = useMemo(
    () => accounts.filter(
      (a) => a.id !== ownAccountId && a.isActive && a.currencyCode === ownAccountCurrencyCode,
    ),
    [accounts, ownAccountId, ownAccountCurrencyCode],
  );

  const titleText = mode === 'link' ? 'Link to existing transfer' : 'Create transfer';
  const bodyText = mode === 'link'
    ? "Pick the account on the other side of this transfer. We'll link this row to the matching transaction we found there."
    : "Pick the account on the other side of this transfer. We'll create a new transfer record between the two accounts.";
  const submitLabel = mode === 'link' ? 'Link transfer' : 'Create transfer';
  const submitInflightLabel = mode === 'link' ? 'Linking…' : 'Creating…';
  const url = mode === 'link' ? TRANSFER_REVIEW_LINK_URL(stagedId) : TRANSFER_REVIEW_CREATE_URL(stagedId);
  const successToast = mode === 'link' ? 'Linked.' : 'Transfer created.';
  const genericError = mode === 'link' ? "Couldn't link. Try again." : "Couldn't create transfer. Try again.";

  async function handleSubmit() {
    if (!otherAccountId || submitting) return;
    setSubmitting(true);
    try {
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ otherAccountId }),
      });
      if (res.ok) {
        toast.success(successToast);
        onActioned();
        onOpenChange(false);
        return;
      }
      if (res.status === 404) {
        toast.error('That row no longer exists. Refreshing.');
        onActioned();
        onOpenChange(false);
        return;
      }
      toast.error(genericError);
    } catch {
      toast.error(genericError);
    } finally {
      setSubmitting(false);
    }
  }

  const selectedAccount = eligible.find((a) => a.id === otherAccountId);

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{titleText}</AlertDialogTitle>
          <AlertDialogDescription>{bodyText}</AlertDialogDescription>
        </AlertDialogHeader>

        <div className="my-4 flex flex-col gap-1">
          <label htmlFor="other-account-trigger" className="text-sm font-medium">
            Other account *
          </label>
          <Popover open={pickerOpen} onOpenChange={setPickerOpen}>
            <PopoverTrigger
              render={
                <Button
                  id="other-account-trigger"
                  type="button"
                  variant="outline"
                  role="combobox"
                  aria-expanded={pickerOpen}
                  className="w-full justify-between"
                >
                  {selectedAccount ? (
                    <span>{selectedAccount.name}</span>
                  ) : (
                    <span className="text-muted-foreground">— Select account —</span>
                  )}
                  <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
                </Button>
              }
            />
            <PopoverContent className="p-0" align="start">
              <Command>
                <CommandInput placeholder="Search accounts…" />
                <CommandList>
                  <CommandEmpty>
                    No eligible accounts. Transfers must be between accounts of the same currency.
                  </CommandEmpty>
                  <CommandGroup>
                    {eligible.map((a) => (
                      <CommandItem
                        key={a.id}
                        value={a.name}
                        onSelect={() => {
                          setOtherAccountId(a.id);
                          setPickerOpen(false);
                        }}
                      >
                        <Check className={cn('mr-2 h-4 w-4', otherAccountId === a.id ? 'opacity-100' : 'opacity-0')} />
                        {a.name}
                      </CommandItem>
                    ))}
                  </CommandGroup>
                </CommandList>
              </Command>
            </PopoverContent>
          </Popover>
        </div>

        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction onClick={handleSubmit} disabled={!otherAccountId || submitting}>
            {submitting ? submitInflightLabel : submitLabel}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
