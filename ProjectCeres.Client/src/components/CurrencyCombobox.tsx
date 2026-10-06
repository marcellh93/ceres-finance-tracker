import { useState } from 'react';
import { Check, ChevronsUpDown } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import { useApi } from '../app/lib/use-api';

export type CurrencyOption = { id: number; code: string; symbol: string };

type Props = {
  value: number | null;
  onChange: (id: number | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  /** Offer only currencies the user has an account in (plus their default currency). */
  inUseOnly?: boolean;
  /** Currency code to show while `value` is null, e.g. the default a report falls back to. */
  fallbackCode?: string;
};

export function CurrencyCombobox({ value, onChange, placeholder = 'Select currency', disabled, className, inUseOnly, fallbackCode }: Props) {
  const [open, setOpen] = useState(false);
  const { data: allCurrencies } = useApi<CurrencyOption[]>('/api/currencies');
  const { data: inUseCurrencies } = useApi<CurrencyOption[]>(inUseOnly ? '/api/currencies?inUse=true' : '');
  const list = (inUseOnly ? inUseCurrencies : allCurrencies) ?? [];
  // Label from the full list so a saved currency that left the limited list still shows.
  const selected =
    (allCurrencies ?? list).find((c) => c.id === value) ??
    (value === null && fallbackCode ? list.find((c) => c.code === fallbackCode) : undefined) ??
    null;

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            type="button"
            variant="outline"
            role="combobox"
            aria-expanded={open}
            disabled={disabled}
            className={cn('justify-between', className)}
          >
            {selected ? (
              <span>
                {selected.symbol} {selected.code}
              </span>
            ) : (
              <span className="text-muted-foreground">{placeholder}</span>
            )}
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent className="p-0" align="start">
        <Command>
          <CommandInput placeholder="Search currencies…" />
          <CommandList>
            <CommandEmpty>No currencies found.</CommandEmpty>
            <CommandGroup>
              {list.map((c) => (
                <CommandItem
                  key={c.id}
                  value={c.code}
                  onSelect={() => {
                    onChange(c.id);
                    setOpen(false);
                  }}
                >
                  <Check className={cn('mr-2 h-4 w-4', selected?.id === c.id ? 'opacity-100' : 'opacity-0')} />
                  {c.symbol} {c.code}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
