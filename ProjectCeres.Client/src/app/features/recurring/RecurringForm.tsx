import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import type { AccountListItemDto } from '../accounts/accounts-api';

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
  accounts: AccountListItemDto[];
  categories: Array<{ id: string; name: string }>;
};

const FREQUENCIES = ['Weekly', 'Biweekly', 'Monthly', 'Annual'];
const BEHAVIOURS = [
  { value: 'SnapToCalendarDay', label: 'Snap to calendar day' },
  {
    value: 'RelativeToLastConfirmation',
    label: 'Relative to last confirmation',
  },
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

  return (
    <div className="space-y-4">
      <div className="space-y-1.5">
        <Label htmlFor="rt-name">Name *</Label>
        <Input
          id="rt-name"
          value={values.name}
          onChange={(e) => set({ name: e.target.value })}
        />
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="rt-account">Account *</Label>
        <Select
          value={values.accountId}
          onValueChange={(v) => set({ accountId: v ?? '' })}
        >
          <SelectTrigger id="rt-account">
            <SelectValue placeholder="Select account" />
          </SelectTrigger>
          <SelectContent>
            {accounts.map((a) => (
              <SelectItem key={a.id} value={a.id}>
                {a.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="rt-category">Category *</Label>
        <Select
          value={values.categoryId}
          onValueChange={(v) => set({ categoryId: v ?? '' })}
        >
          <SelectTrigger id="rt-category">
            <SelectValue placeholder="Select category" />
          </SelectTrigger>
          <SelectContent>
            {categories.map((c) => (
              <SelectItem key={c.id} value={c.id}>
                {c.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="rt-amount">Estimated amount</Label>
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
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="rt-frequency">Frequency *</Label>
        <Select
          value={values.frequency}
          onValueChange={(v) => set({ frequency: v ?? '' })}
        >
          <SelectTrigger id="rt-frequency">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {FREQUENCIES.map((f) => (
              <SelectItem key={f} value={f}>
                {f}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="rt-behaviour">Reminder behaviour *</Label>
        <Select
          value={values.reminderBehaviour}
          onValueChange={(v) => set({ reminderBehaviour: v ?? '' })}
        >
          <SelectTrigger id="rt-behaviour">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {BEHAVIOURS.map((b) => (
              <SelectItem key={b.value} value={b.value}>
                {b.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {showDayOfWeek(values.frequency, values.reminderBehaviour) && (
        <div className="space-y-1.5">
          <Label htmlFor="rt-dow">Day of week *</Label>
          <Select
            value={values.dayOfPeriod?.toString() ?? ''}
            onValueChange={(v) => set({ dayOfPeriod: parseInt(v ?? '', 10) })}
          >
            <SelectTrigger id="rt-dow">
              <SelectValue placeholder="Pick a day" />
            </SelectTrigger>
            <SelectContent>
              {WEEKDAYS.map((d) => (
                <SelectItem key={d.value} value={d.value.toString()}>
                  {d.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      )}

      {showDayOfMonth(values.frequency, values.reminderBehaviour) && (
        <div className="space-y-1.5">
          <Label htmlFor="rt-dom">Day of month *</Label>
          <Input
            id="rt-dom"
            type="number"
            min={1}
            max={31}
            value={values.dayOfPeriod?.toString() ?? ''}
            onChange={(e) =>
              set({ dayOfPeriod: e.target.value ? Number(e.target.value) : null })
            }
          />
          <p className="text-xs text-muted-foreground">
            1–31. Snaps to the last day if the month is shorter.
          </p>
        </div>
      )}

      <div className="space-y-1.5">
        <Label htmlFor="rt-nextdue">Next due date *</Label>
        <Input
          id="rt-nextdue"
          type="date"
          value={values.nextDueDate}
          onChange={(e) => set({ nextDueDate: e.target.value })}
        />
      </div>
    </div>
  );
}
