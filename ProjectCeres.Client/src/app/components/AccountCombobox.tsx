import { Check, ChevronsUpDown, X } from 'lucide-react';
import { useState, type MouseEvent } from 'react';
import { Button } from '@/components/ui/button';
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import type { AccountOptionDto } from '../features/movements/movements-api';

type Props = {
  accounts: AccountOptionDto[];
  value: string | null;
  onChange: (accountId: string) => void;
  placeholder: string;
  filter?: (account: AccountOptionDto) => boolean;
  disabled?: boolean;
  className?: string;
  /**
   * When provided AND a value is selected, render an inline ✕ on the trigger
   * that calls this callback. Click stops propagation so the popover stays
   * closed. Omit on surfaces where clearing is not allowed.
   */
  onClear?: () => void;
};

export function AccountCombobox({ accounts, value, onChange, placeholder, filter, disabled, className = 'w-full', onClear }: Props) {
  const [open, setOpen] = useState(false);
  const filtered = filter ? accounts.filter(filter) : accounts;
  const selected = accounts.find((a) => a.id === value) ?? null;
  const showClear = !!onClear && !!selected && !disabled;

  function handleClear(e: MouseEvent<HTMLButtonElement>) {
    e.preventDefault();
    e.stopPropagation();
    onClear?.();
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            variant="outline"
            role="combobox"
            aria-expanded={open}
            disabled={disabled}
            className={cn('justify-between', className)}
          >
            {selected ? selected.name : <span className="text-muted-foreground">{placeholder}</span>}
            <span className="ml-2 flex shrink-0 items-center gap-1.5">
              {showClear && (
                <button
                  type="button"
                  onClick={handleClear}
                  aria-label="Clear selection"
                  className="rounded-sm p-0.5 text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
                >
                  <X className="h-4 w-4" />
                </button>
              )}
              <ChevronsUpDown className="h-4 w-4 opacity-50" />
            </span>
          </Button>
        }
      />
      <PopoverContent className="p-0" align="start">
        <Command>
          <CommandInput placeholder="Search accounts…" />
          <CommandList>
            <CommandEmpty>No accounts found.</CommandEmpty>
            <CommandGroup>
              {filtered.map((account) => (
                <CommandItem
                  key={account.id}
                  value={account.name}
                  onSelect={() => {
                    onChange(account.id);
                    setOpen(false);
                  }}
                >
                  <Check className={cn('mr-2 h-4 w-4', value === account.id ? 'opacity-100' : 'opacity-0')} />
                  {account.name}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
