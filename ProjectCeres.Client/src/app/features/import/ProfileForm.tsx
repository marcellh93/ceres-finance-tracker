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
import type { ImportFormat, ProfileFormValues } from './import-api';

type SubmitResult = { ok: true } | { ok: false };

type Props = {
  mode: 'create' | 'edit';
  initialValues: ProfileFormValues;
  onSubmit: (values: ProfileFormValues) => Promise<SubmitResult>;
  onCancel: () => void;
};

const FORMAT_OPTIONS: { value: ImportFormat; label: string }[] = [
  { value: 'Csv',   label: 'CSV (.csv)' },
  { value: 'Excel', label: 'Excel (.xlsx)' },
];

const FORMAT_LOCKED_TOOLTIP =
  'The format cannot be changed after creation. Create a new profile if you need a different format.';

function shallowEqual(a: ProfileFormValues, b: ProfileFormValues): boolean {
  return (
    a.name              === b.name              &&
    a.format            === b.format            &&
    a.sheetName         === b.sheetName         &&
    a.dateColumn        === b.dateColumn        &&
    a.amountColumn      === b.amountColumn      &&
    a.descriptionColumn === b.descriptionColumn &&
    a.categoryColumn    === b.categoryColumn
  );
}

export function ProfileForm({ mode, initialValues, onSubmit, onCancel }: Props) {
  const [snapshot, setSnapshot] = useState<ProfileFormValues>(initialValues);
  const [values, setValues]     = useState<ProfileFormValues>(initialValues);
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

  const selectedFormat = FORMAT_OPTIONS.find((o) => o.value === values.format);

  return (
    <TooltipProvider delay={200}>
      <form onSubmit={handleSubmit} className="space-y-6">
        <div className="space-y-1.5">
          <Label htmlFor="profileName">Name</Label>
          <Input
            id="profileName"
            aria-label="Name"
            value={values.name}
            onChange={(e) => setValues((cur) => ({ ...cur, name: e.target.value }))}
            required
            maxLength={100}
            placeholder="e.g. Sabadell Checking"
          />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="profileFormat">Format</Label>
          {mode === 'edit' ? (
            <div
              id="profileFormat"
              aria-label="Format"
              className="flex h-9 w-full items-center justify-between rounded-md border border-input bg-muted/40 px-3 text-sm"
            >
              <span>{selectedFormat?.label ?? '—'}</span>
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
                  {FORMAT_LOCKED_TOOLTIP}
                </TooltipContent>
              </Tooltip>
            </div>
          ) : (
            <FormatCombobox
              id="profileFormat"
              value={values.format}
              onChange={(format) =>
                setValues((cur) => ({
                  ...cur,
                  format,
                  // Excel-only field clears when switching back to Csv.
                  sheetName: format === 'Csv' ? '' : cur.sheetName,
                }))
              }
            />
          )}
        </div>

        {values.format === 'Excel' ? (
          <div className="space-y-1.5">
            <Label htmlFor="profileSheet">Sheet name</Label>
            <Input
              id="profileSheet"
              aria-label="Sheet name"
              value={values.sheetName}
              onChange={(e) => setValues((cur) => ({ ...cur, sheetName: e.target.value }))}
              maxLength={100}
              placeholder="Leave blank to read the first sheet"
            />
            <p className="text-xs text-muted-foreground">
              Optional. Leave blank to read whichever sheet appears first.
            </p>
          </div>
        ) : null}

        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="dateColumn">Date column</Label>
            <Input
              id="dateColumn"
              aria-label="Date column"
              value={values.dateColumn}
              onChange={(e) => setValues((cur) => ({ ...cur, dateColumn: e.target.value }))}
              required
              maxLength={100}
              placeholder="e.g. Fecha"
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="amountColumn">Amount column</Label>
            <Input
              id="amountColumn"
              aria-label="Amount column"
              value={values.amountColumn}
              onChange={(e) => setValues((cur) => ({ ...cur, amountColumn: e.target.value }))}
              required
              maxLength={100}
              placeholder="e.g. Importe"
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="descriptionColumn">Description column</Label>
            <Input
              id="descriptionColumn"
              aria-label="Description column"
              value={values.descriptionColumn}
              onChange={(e) => setValues((cur) => ({ ...cur, descriptionColumn: e.target.value }))}
              required
              maxLength={100}
              placeholder="e.g. Concepto"
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="categoryColumn">Category column</Label>
            <Input
              id="categoryColumn"
              aria-label="Category column"
              value={values.categoryColumn}
              onChange={(e) => setValues((cur) => ({ ...cur, categoryColumn: e.target.value }))}
              maxLength={100}
              placeholder="Optional"
            />
            <p className="text-xs text-muted-foreground">
              Optional. Leave blank if your bank doesn't include a category column.
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2 pt-2">
          <Button type="submit" disabled={!isDirty || submitting}>
            {submitting ? 'Saving…' : 'Save'}
          </Button>
          <Button
            type="button"
            variant="outline"
            onClick={onCancel}
            disabled={submitting}
          >
            Cancel
          </Button>
        </div>
      </form>
    </TooltipProvider>
  );
}

function FormatCombobox({
  id,
  value,
  onChange,
}: {
  id: string;
  value: ImportFormat;
  onChange: (v: ImportFormat) => void;
}) {
  const [open, setOpen] = useState(false);
  const selected = FORMAT_OPTIONS.find((o) => o.value === value);
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            id={id}
            type="button"
            variant="outline"
            role="combobox"
            aria-label="Format"
            aria-expanded={open}
            className="w-full justify-between"
          >
            <span>{selected?.label ?? 'Select format'}</span>
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
              {FORMAT_OPTIONS.map((opt) => (
                <CommandItem
                  key={opt.value}
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
