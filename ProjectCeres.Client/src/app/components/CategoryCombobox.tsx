import { Check, ChevronsUpDown, X } from 'lucide-react';
import { useState, type MouseEvent } from 'react';
import { Button } from '@/components/ui/button';
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import type { CategoryOptionDto } from '../features/movements/movements-api';

type Props = {
  categories: CategoryOptionDto[];
  value: string | null;
  onChange: (categoryId: string) => void;
  placeholder: string;
  disabled?: boolean;
  className?: string;
  /**
   * When provided AND a value is selected, render an inline ✕ on the trigger
   * that calls this callback. Click stops propagation so the popover stays
   * closed. Omit on surfaces where clearing is not allowed.
   */
  onClear?: () => void;
};

export function CategoryCombobox({ categories, value, onChange, placeholder, disabled, className = 'w-full', onClear }: Props) {
  const [open, setOpen] = useState(false);
  const selected = categories.find((c) => c.id === value) ?? null;
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
              variant="outline"
              role="combobox"
              aria-label={selected?.name ?? placeholder}
              aria-expanded={open}
              disabled={disabled}
              className={cn('w-full justify-start text-left font-normal', showClear ? 'pr-14' : 'pr-8')}
            >
              {selected ? <span className="truncate">{selected.name}</span> : <span className="text-muted-foreground">{placeholder}</span>}
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
        <ChevronsUpDown className="pointer-events-none absolute right-2.5 top-1/2 h-4 w-4 -translate-y-1/2 opacity-50" />
      </div>
      <PopoverContent className="p-0" align="start">
        <Command>
          <CommandInput placeholder="Search categories…" />
          <CommandList>
            <CommandEmpty>No categories found.</CommandEmpty>
            <CommandGroup>
              {categories.map((category) => (
                <CommandItem
                  key={category.id}
                  value={category.name}
                  onSelect={() => {
                    onChange(category.id);
                    setOpen(false);
                  }}
                >
                  <Check className={cn('mr-2 h-4 w-4', value === category.id ? 'opacity-100' : 'opacity-0')} />
                  <span className="flex-1">{category.name}</span>
                  <span className="text-xs text-muted-foreground">{category.categoryTypeName}</span>
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
