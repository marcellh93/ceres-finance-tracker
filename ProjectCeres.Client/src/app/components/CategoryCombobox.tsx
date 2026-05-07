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
            <span className="ml-2 flex shrink-0 items-center gap-1">
              {showClear && (
                <button
                  type="button"
                  onClick={handleClear}
                  aria-label="Clear selection"
                  className="-mr-1 rounded-sm p-0.5 text-muted-foreground opacity-70 hover:bg-muted hover:opacity-100 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
                >
                  <X className="h-3.5 w-3.5" />
                </button>
              )}
              <ChevronsUpDown className="h-4 w-4 opacity-50" />
            </span>
          </Button>
        }
      />
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
