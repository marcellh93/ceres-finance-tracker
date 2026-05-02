import { useState } from 'react';
import { Check, ChevronsUpDown, Info } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Command,
  CommandGroup,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import { cn } from '@/lib/utils';
import type {
  CategoryFormValues,
  CategoryTypeDto,
  LifestyleTag,
} from './categories-api';

type SubmitResult = { ok: true } | { ok: false };

type Props = {
  mode: 'create' | 'edit';
  initialValues: CategoryFormValues;
  categoryTypes: CategoryTypeDto[];
  onSubmit: (values: CategoryFormValues) => Promise<SubmitResult>;
};

const LIFESTYLE_OPTIONS: { value: LifestyleTag | null; label: string }[] = [
  { value: null,      label: 'None' },
  { value: 'Needs',   label: 'Needs' },
  { value: 'Wants',   label: 'Wants' },
  { value: 'Savings', label: 'Savings' },
];

const TYPE_LOCKED_TOOLTIP =
  'The type cannot be changed after creation. Create a new category if you need a different type.';

function shallowEqual(a: CategoryFormValues, b: CategoryFormValues): boolean {
  return (
    a.name           === b.name           &&
    a.categoryTypeId === b.categoryTypeId &&
    a.lifestyleTag   === b.lifestyleTag
  );
}

export function CategoryForm({ mode, initialValues, categoryTypes, onSubmit }: Props) {
  const [snapshot, setSnapshot] = useState<CategoryFormValues>(initialValues);
  const [values, setValues] = useState<CategoryFormValues>(initialValues);
  const [submitting, setSubmitting] = useState(false);

  const isDirty = !shallowEqual(values, snapshot);

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!isDirty || submitting) return;
    setSubmitting(true);
    const result = await onSubmit(values);
    setSubmitting(false);
    if (result.ok) {
      setSnapshot(values);
    }
  }

  const selectedType = categoryTypes.find((t) => t.id === values.categoryTypeId);
  const selectedLifestyleLabel =
    LIFESTYLE_OPTIONS.find((o) => o.value === values.lifestyleTag)?.label ?? 'None';

  return (
    <TooltipProvider delay={200}>
      <form onSubmit={handleSubmit} className="space-y-6">
        <div className="space-y-1.5">
          <Label htmlFor="categoryName">Name</Label>
          <Input
            id="categoryName"
            aria-label="Name"
            value={values.name}
            onChange={(e) => setValues((cur) => ({ ...cur, name: e.target.value }))}
            required
            maxLength={100}
          />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="categoryType">Type</Label>
          {mode === 'edit' ? (
            // CategoryType is server-immutable — see categories-api.ts comment on
            // UpdateCategoryRequest. The disabled UI is a hint; the structural
            // guarantee is the API DTO.
            <div
              id="categoryType"
              aria-label="Type"
              className="flex h-9 w-full items-center justify-between rounded-md border border-input bg-muted/40 px-3 text-sm"
            >
              <span>{selectedType?.name ?? '—'}</span>
              <Tooltip>
                <TooltipTrigger
                  render={
                    <button
                      type="button"
                      aria-label="Why is this locked?"
                      className="text-muted-foreground hover:text-foreground transition-colors"
                    >
                      <Info className="h-3.5 w-3.5" />
                    </button>
                  }
                />
                <TooltipContent className="max-w-xs">
                  {TYPE_LOCKED_TOOLTIP}
                </TooltipContent>
              </Tooltip>
            </div>
          ) : (
            <TypeCombobox
              id="categoryType"
              value={values.categoryTypeId}
              types={categoryTypes}
              onChange={(id) =>
                setValues((cur) => ({ ...cur, categoryTypeId: id }))
              }
            />
          )}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="lifestyleTag">Lifestyle tag</Label>
          <LifestyleCombobox
            id="lifestyleTag"
            value={values.lifestyleTag}
            renderTrigger={() => selectedLifestyleLabel}
            onChange={(v) => setValues((cur) => ({ ...cur, lifestyleTag: v }))}
          />
          <p className="text-xs text-muted-foreground">
            Optional. Used by the Expense Breakdown report.
          </p>
        </div>

        <div className="flex items-center gap-2 pt-2">
          <Button type="submit" disabled={!isDirty || submitting}>
            {submitting ? 'Saving…' : 'Save'}
          </Button>
        </div>
      </form>
    </TooltipProvider>
  );
}

// ---------------------------------------------------------------------------
// Local pickers — Popover+Command (locked SPA idiom; no shadcn <Select>).
// ---------------------------------------------------------------------------

function TypeCombobox({
  id,
  value,
  types,
  onChange,
}: {
  id: string;
  value: number;
  types: CategoryTypeDto[];
  onChange: (id: number) => void;
}) {
  const [open, setOpen] = useState(false);
  const selected = types.find((t) => t.id === value);
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            id={id}
            type="button"
            variant="outline"
            role="combobox"
            aria-label="Type"
            aria-expanded={open}
            className="w-full justify-between"
          >
            <span>{selected?.name ?? 'Select type'}</span>
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent
        className="p-0 w-(--anchor-width) min-w-(--anchor-width)"
        align="start"
      >
        <Command>
          <CommandList>
            <CommandGroup>
              {types.map((t) => (
                <CommandItem
                  key={t.id}
                  value={t.name}
                  className="whitespace-nowrap"
                  onSelect={() => {
                    onChange(t.id);
                    setOpen(false);
                  }}
                >
                  <Check
                    className={cn(
                      'mr-2 h-4 w-4',
                      t.id === value ? 'opacity-100' : 'opacity-0',
                    )}
                  />
                  {t.name}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}

function LifestyleCombobox({
  id,
  value,
  renderTrigger,
  onChange,
}: {
  id: string;
  value: LifestyleTag | null;
  renderTrigger: () => string;
  onChange: (v: LifestyleTag | null) => void;
}) {
  const [open, setOpen] = useState(false);
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            id={id}
            type="button"
            variant="outline"
            role="combobox"
            aria-label="Lifestyle tag"
            aria-expanded={open}
            className="w-full justify-between"
          >
            <span>{renderTrigger()}</span>
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent
        className="p-0 w-(--anchor-width) min-w-(--anchor-width)"
        align="start"
      >
        <Command>
          <CommandList>
            <CommandGroup>
              {LIFESTYLE_OPTIONS.map((opt) => (
                <CommandItem
                  key={opt.label}
                  value={opt.label}
                  className="whitespace-nowrap"
                  onSelect={() => {
                    onChange(opt.value);
                    setOpen(false);
                  }}
                >
                  <Check
                    className={cn(
                      'mr-2 h-4 w-4',
                      opt.value === value ? 'opacity-100' : 'opacity-0',
                    )}
                  />
                  {opt.label}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
