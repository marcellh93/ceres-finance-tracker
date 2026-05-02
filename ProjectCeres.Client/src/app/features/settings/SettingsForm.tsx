import { useState } from 'react';
import { Check, ChevronsUpDown } from 'lucide-react';
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
import { cn } from '@/lib/utils';
import type {
  CurrencyOptionDto,
  DateFormat,
  NumberFormat,
  SettingsFormValues,
} from './settings-api';

type SubmitResult = { ok: true } | { ok: false };

type Props = {
  initialValues: SettingsFormValues;
  currencies: CurrencyOptionDto[];
  onSubmit: (values: SettingsFormValues) => Promise<SubmitResult>;
};

const NUMBER_FORMATS: { value: NumberFormat; label: string }[] = [
  { value: 'comma_decimal',  label: '1.234,56  (comma decimal — EU)' },
  { value: 'period_decimal', label: '1,234.56  (period decimal — US)' },
];

const DATE_FORMATS: { value: DateFormat; label: string }[] = [
  { value: 'DD/MM/YYYY', label: 'DD/MM/YYYY' },
  { value: 'MM/DD/YYYY', label: 'MM/DD/YYYY' },
  { value: 'YYYY-MM-DD', label: 'YYYY-MM-DD' },
];

function shallowEqual(a: SettingsFormValues, b: SettingsFormValues): boolean {
  return (
    a.numberFormat       === b.numberFormat       &&
    a.dateFormat         === b.dateFormat         &&
    a.defaultCurrencyId  === b.defaultCurrencyId  &&
    a.periodStartDay     === b.periodStartDay
  );
}

function clampStartDay(raw: string): number {
  const n = Number.parseInt(raw, 10);
  if (Number.isNaN(n)) return 1;
  if (n < 1) return 1;
  if (n > 31) return 31;
  return n;
}

export function SettingsForm({ initialValues, currencies, onSubmit }: Props) {
  const [snapshot, setSnapshot] = useState<SettingsFormValues>(initialValues);
  const [values, setValues] = useState<SettingsFormValues>(initialValues);
  const [submitting, setSubmitting] = useState(false);

  const isDirty = !shallowEqual(values, snapshot);

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!isDirty || submitting) return;
    setSubmitting(true);
    const result = await onSubmit(values);
    setSubmitting(false);
    if (result.ok) {
      // Re-baseline so isDirty becomes false and Save greys back out.
      setSnapshot(values);
    }
    // On ok:false the snapshot stays old, isDirty stays true, user can retry.
  }

  function handleReset() {
    if (submitting) return;
    setValues(snapshot);
  }

  const numberFormatLabel  = NUMBER_FORMATS.find((f) => f.value === values.numberFormat)?.label  ?? values.numberFormat;
  const dateFormatLabel    = DATE_FORMATS.find((f) => f.value === values.dateFormat)?.label      ?? values.dateFormat;
  const selectedCurrency   = currencies.find((c) => c.id === values.defaultCurrencyId);

  return (
    <form onSubmit={handleSubmit} className="space-y-6">
      <div className="space-y-1.5">
        <Label htmlFor="numberFormat">Number format</Label>
        <EnumCombobox
          id="numberFormat"
          ariaLabel="Number format"
          value={values.numberFormat}
          options={NUMBER_FORMATS}
          renderTrigger={() => numberFormatLabel}
          onChange={(v) => setValues((cur) => ({ ...cur, numberFormat: v }))}
        />
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="dateFormat">Date format</Label>
        <EnumCombobox
          id="dateFormat"
          ariaLabel="Date format"
          value={values.dateFormat}
          options={DATE_FORMATS}
          renderTrigger={() => dateFormatLabel}
          onChange={(v) => setValues((cur) => ({ ...cur, dateFormat: v }))}
        />
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="defaultCurrencyId">Default currency</Label>
        <CurrencyPicker
          id="defaultCurrencyId"
          ariaLabel="Default currency"
          value={values.defaultCurrencyId}
          currencies={currencies}
          renderTrigger={() =>
            selectedCurrency
              ? `${selectedCurrency.code} (${selectedCurrency.symbol})`
              : ''
          }
          onChange={(id) => setValues((cur) => ({ ...cur, defaultCurrencyId: id }))}
        />
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="periodStartDay">Period start day</Label>
        <Input
          id="periodStartDay"
          aria-label="Period start day"
          type="number"
          min={1}
          max={31}
          value={values.periodStartDay}
          onChange={(e) =>
            setValues((cur) => ({ ...cur, periodStartDay: clampStartDay(e.target.value) }))
          }
        />
        <p className="text-xs text-muted-foreground">
          Day of month (1–31) when monthly cycles start.
        </p>
      </div>

      <div className="flex items-center gap-2 pt-2">
        <Button type="submit" disabled={!isDirty || submitting}>
          {submitting ? 'Saving…' : 'Save'}
        </Button>
        <Button
          type="button"
          variant="ghost"
          onClick={handleReset}
          disabled={!isDirty || submitting}
        >
          Reset
        </Button>
      </div>
    </form>
  );
}

// ---------------------------------------------------------------------------
// Local generic enum combobox — Popover + Command without search input.
// Same visual grammar as CurrencyCombobox / CategoryCombobox, but for closed
// enumerations of 2-5 options where a search input would be friction.
// ---------------------------------------------------------------------------

type EnumComboboxProps<T extends string> = {
  id: string;
  ariaLabel: string;
  value: T;
  options: { value: T; label: string }[];
  renderTrigger: () => string;
  onChange: (value: T) => void;
};

function EnumCombobox<T extends string>({
  id,
  ariaLabel,
  value,
  options,
  renderTrigger,
  onChange,
}: EnumComboboxProps<T>) {
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
            aria-label={ariaLabel}
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
              {options.map((opt) => (
                <CommandItem
                  key={opt.value}
                  value={opt.label}
                  onSelect={() => {
                    onChange(opt.value);
                    setOpen(false);
                  }}
                  className="whitespace-nowrap"
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

// ---------------------------------------------------------------------------
// Currency picker — same shape as CurrencyCombobox but takes the list as a
// prop instead of fetching internally, so SettingsPage stays the single
// owner of the /api/currencies fetch.
// ---------------------------------------------------------------------------

type CurrencyPickerProps = {
  id: string;
  ariaLabel: string;
  value: number;
  currencies: CurrencyOptionDto[];
  renderTrigger: () => string;
  onChange: (id: number) => void;
};

function CurrencyPicker({
  id,
  ariaLabel,
  value,
  currencies,
  renderTrigger,
  onChange,
}: CurrencyPickerProps) {
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
            aria-label={ariaLabel}
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
              {currencies.map((c) => (
                <CommandItem
                  key={c.id}
                  value={c.code}
                  className="whitespace-nowrap"
                  onSelect={() => {
                    onChange(c.id);
                    setOpen(false);
                  }}
                >
                  <Check
                    className={cn(
                      'mr-2 h-4 w-4',
                      c.id === value ? 'opacity-100' : 'opacity-0',
                    )}
                  />
                  {c.code} ({c.symbol})
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
