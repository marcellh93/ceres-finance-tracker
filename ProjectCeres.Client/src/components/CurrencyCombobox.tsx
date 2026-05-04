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
};

export function CurrencyCombobox({ value, onChange, placeholder = 'Select currency', disabled, className }: Props) {
  const [open, setOpen] = useState(false);
  const { data: currencies } = useApi<CurrencyOption[]>('/api/currencies');
  const list = currencies ?? [];
  const selected = list.find((c) => c.id === value) ?? null;

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
                  <Check className={cn('mr-2 h-4 w-4', value === c.id ? 'opacity-100' : 'opacity-0')} />
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
