import { Check, ChevronsUpDown, X } from 'lucide-react';
import { useState, type MouseEvent } from 'react';
import { Button } from '@/components/ui/button';
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import type { GoalBudgetListItemDto } from '../features/budgets/budgets-api';

type Props = {
  budgets: GoalBudgetListItemDto[];
  value: string | null;
  onChange: (budgetId: string) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  /**
   * When provided AND a value is selected, render an inline ✕ on the trigger
   * that calls this callback. Click stops propagation so the popover stays
   * closed. Omit on surfaces where clearing is not allowed.
   */
  onClear?: () => void;
  /** Forwarded to the trigger Button for htmlFor association from a Field label. */
  id?: string;
};

export function BudgetCombobox({
  budgets,
  value,
  onChange,
  placeholder = 'No budget',
  disabled,
  className = 'w-full',
  onClear,
  id,
}: Props) {
  const [open, setOpen] = useState(false);
  const selected = budgets.find((b) => b.id === value) ?? null;
  const showClear = !!onClear && !!selected && !disabled;

  function handleClear(e: MouseEvent<HTMLButtonElement>) {
    e.preventDefault();
    e.stopPropagation();
    onClear?.();
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <div className={cn('relative', className)}>
        <PopoverTrigger
          render={
            <Button
              id={id}
              variant="outline"
              role="combobox"
              aria-expanded={open}
              disabled={disabled}
              className={cn('w-full justify-between', showClear && 'pr-12')}
            >
              {selected ? selected.name : <span className="text-muted-foreground">{placeholder}</span>}
              <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
            </Button>
          }
        />
        {showClear && (
          <button
            type="button"
            onClick={handleClear}
            aria-label="Clear selection"
            className="absolute right-8 top-1/2 -translate-y-1/2 rounded-sm p-0.5 text-muted-foreground transition-colors hover:bg-muted hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
          >
            <X className="h-4 w-4" />
          </button>
        )}
      </div>
      <PopoverContent className="p-0" align="start">
        <Command>
          <CommandInput placeholder="Search budgets…" />
          <CommandList>
            <CommandEmpty>No budgets found.</CommandEmpty>
            <CommandGroup>
              {budgets.map((b) => (
                <CommandItem
                  key={b.id}
                  value={b.name}
                  onSelect={() => {
                    onChange(b.id);
                    setOpen(false);
                  }}
                >
                  <Check className={cn('mr-2 h-4 w-4', value === b.id ? 'opacity-100' : 'opacity-0')} />
                  {b.name}
                  {!b.isActive ? ' (archived)' : ''}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
