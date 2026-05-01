import { Check, ChevronsUpDown } from 'lucide-react';
import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Command, CommandGroup, CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import { MOVEMENT_TYPE_LABEL_PLURAL } from './movement-type-display';

export type TypeFilterValue = 'all' | 'transaction' | 'transfer' | 'liabilitypayment';

const OPTIONS: Array<{ value: TypeFilterValue; label: string }> = [
  { value: 'all', label: 'All' },
  { value: 'transaction', label: MOVEMENT_TYPE_LABEL_PLURAL.Transaction },
  { value: 'transfer', label: MOVEMENT_TYPE_LABEL_PLURAL.Transfer },
  { value: 'liabilitypayment', label: MOVEMENT_TYPE_LABEL_PLURAL.LiabilityPayment },
];

type Props = {
  id?: string;
  value: TypeFilterValue;
  onChange: (value: TypeFilterValue) => void;
};

export function TypeFilterCombobox({ id, value, onChange }: Props) {
  const [open, setOpen] = useState(false);
  const selected = OPTIONS.find((o) => o.value === value) ?? OPTIONS[0];

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            id={id}
            variant="outline"
            role="combobox"
            aria-expanded={open}
            className="w-full justify-between"
          >
            {selected.label}
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent className="p-0" align="start">
        <Command>
          <CommandList>
            <CommandGroup>
              {OPTIONS.map((option) => (
                <CommandItem
                  key={option.value}
                  value={option.label}
                  onSelect={() => {
                    onChange(option.value);
                    setOpen(false);
                  }}
                >
                  <Check
                    className={cn(
                      'mr-2 h-4 w-4',
                      value === option.value ? 'opacity-100' : 'opacity-0',
                    )}
                  />
                  {option.label}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
