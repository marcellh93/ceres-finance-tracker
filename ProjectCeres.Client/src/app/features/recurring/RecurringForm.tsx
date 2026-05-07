import { useState } from 'react';
import { Check, ChevronsUpDown } from 'lucide-react';
import { Input } from '@/components/ui/input';
import { Button } from '@/components/ui/button';
import { Command, CommandEmpty, CommandGroup, CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import { DatePickerField } from '../../../components/DatePickerField';
import { AccountCombobox } from '../../components/AccountCombobox';
import { CategoryCombobox } from '../../components/CategoryCombobox';
import { Field } from '../../components/Field';
import type { AccountOptionDto, CategoryOptionDto } from '../movements/movements-api';

export type RecurringFormValues = {
  name: string;
  accountId: string;
  categoryId: string;
  estimatedAmount: string; // empty string = null on submit
  frequency: string;
  reminderBehaviour: string;
  dayOfPeriod: number | null;
  nextDueDate: string;
};

type Props = {
  values: RecurringFormValues;
  onChange: (updated: RecurringFormValues) => void;
  accounts: AccountOptionDto[];
  categories: CategoryOptionDto[];
};

const FREQUENCIES = ['Weekly', 'Biweekly', 'Monthly', 'Annual'];
const BEHAVIOURS = [
  { value: 'SnapToCalendarDay', label: 'Snap to calendar day' },
  { value: 'RelativeToLastConfirmation', label: 'Relative to last confirmation' },
  { value: 'ManualDate', label: 'Manual date' },
];
const WEEKDAYS = [
  { value: 1, label: 'Monday' },
  { value: 2, label: 'Tuesday' },
  { value: 3, label: 'Wednesday' },
  { value: 4, label: 'Thursday' },
  { value: 5, label: 'Friday' },
  { value: 6, label: 'Saturday' },
  { value: 7, label: 'Sunday' },
];

function showDayOfWeek(frequency: string, behaviour: string) {
  return (
    behaviour === 'SnapToCalendarDay' &&
    (frequency === 'Weekly' || frequency === 'Biweekly')
  );
}

function showDayOfMonth(frequency: string, behaviour: string) {
  return behaviour === 'SnapToCalendarDay' && frequency === 'Monthly';
}

function InlineCombobox({
  items,
  value,
  onSelect,
  placeholder,
}: {
  items: { value: string; label: string }[];
  value: string;
  onSelect: (v: string) => void;
  placeholder?: string;
}) {
  const [open, setOpen] = useState(false);
  const selected = items.find((i) => i.value === value);

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            type="button"
            variant="outline"
            role="combobox"
            aria-expanded={open}
            className="w-full justify-between"
          >
            {selected ? selected.label : <span className="text-muted-foreground">{placeholder ?? 'Select…'}</span>}
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent className="p-0 w-(--anchor-width) min-w-max" align="start">
        <Command>
          <CommandList>
            <CommandEmpty>No options found.</CommandEmpty>
            <CommandGroup>
              {items.map((item) => (
                <CommandItem
                  key={item.value}
                  value={item.value}
                  onSelect={() => {
                    onSelect(item.value);
                    setOpen(false);
                  }}
                >
                  <Check className={cn('mr-2 h-4 w-4', value === item.value ? 'opacity-100' : 'opacity-0')} />
                  {item.label}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}

export function RecurringForm({
  values,
  onChange,
  accounts,
  categories,
}: Props) {
  function set(patch: Partial<RecurringFormValues>) {
    const next = { ...values, ...patch };
    if (
      !showDayOfWeek(next.frequency, next.reminderBehaviour) &&
      !showDayOfMonth(next.frequency, next.reminderBehaviour)
    ) {
      next.dayOfPeriod = null;
    }
    onChange(next);
  }

  const frequencyItems = FREQUENCIES.map((f) => ({ value: f, label: f }));
  const weekdayItems = WEEKDAYS.map((d) => ({ value: String(d.value), label: d.label }));

  return (
    <div className="space-y-5">
      <Field label="Name *" htmlFor="rt-name">
        <Input
          id="rt-name"
          value={values.name}
          onChange={(e) => set({ name: e.target.value })}
        />
      </Field>

      <Field label="Account *">
        <AccountCombobox
          accounts={accounts}
          value={values.accountId || null}
          onChange={(id) => set({ accountId: id })}
          onClear={() => set({ accountId: '' })}
          placeholder="Select account"
        />
      </Field>

      <Field label="Category *">
        <CategoryCombobox
          categories={categories}
          value={values.categoryId || null}
          onChange={(id) => set({ categoryId: id })}
          onClear={() => set({ categoryId: '' })}
          placeholder="Select category"
        />
      </Field>

      <Field label="Estimated amount" htmlFor="rt-amount">
        <Input
          id="rt-amount"
          type="number"
          min={0}
          step="0.01"
          value={values.estimatedAmount}
          onChange={(e) => set({ estimatedAmount: e.target.value })}
        />
        <p className="text-xs text-muted-foreground">
          Leave blank if the amount varies each time.
        </p>
      </Field>

      <Field label="Frequency *">
        <InlineCombobox
          items={frequencyItems}
          value={values.frequency}
          onSelect={(v) => set({ frequency: v })}
        />
      </Field>

      <Field label="Reminder behaviour *">
        <InlineCombobox
          items={BEHAVIOURS}
          value={values.reminderBehaviour}
          onSelect={(v) => set({ reminderBehaviour: v })}
        />
      </Field>

      {showDayOfWeek(values.frequency, values.reminderBehaviour) && (
        <Field label="Day of week *">
          <InlineCombobox
            items={weekdayItems}
            value={values.dayOfPeriod != null ? String(values.dayOfPeriod) : ''}
            onSelect={(v) => set({ dayOfPeriod: parseInt(v, 10) })}
            placeholder="Pick a day"
          />
        </Field>
      )}

      {showDayOfMonth(values.frequency, values.reminderBehaviour) && (
        <Field label="Day of month *" htmlFor="rt-dom">
          <Input
            id="rt-dom"
            type="text"
            inputMode="numeric"
            value={values.dayOfPeriod?.toString() ?? ''}
            onChange={(e) => {
              const digits = e.target.value.replace(/[^0-9]/g, '');
              if (!digits) { set({ dayOfPeriod: null }); return; }
              const v = Math.min(31, Math.max(1, parseInt(digits, 10)));
              set({ dayOfPeriod: v });
            }}
          />
          <p className="text-xs text-muted-foreground">
            1–31. Snaps to the last day if the month is shorter.
          </p>
        </Field>
      )}

      <Field label="Next due date *" htmlFor="rt-nextdue">
        <DatePickerField
          id="rt-nextdue"
          value={values.nextDueDate || null}
          onChange={(v) => set({ nextDueDate: v ?? '' })}
          hideClear
        />
      </Field>
    </div>
  );
}
