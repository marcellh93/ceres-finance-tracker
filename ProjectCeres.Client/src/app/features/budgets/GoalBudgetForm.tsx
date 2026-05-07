import { useEffect, useMemo, useState } from 'react';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Textarea } from '@/components/ui/textarea';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
  InputGroupText,
} from '@/components/ui/input-group';
import { AccountCombobox } from '@/app/components/AccountCombobox';
import { CurrencyCombobox } from '@/components/CurrencyCombobox';
import { DatePickerField } from '@/components/DatePickerField';
import {
  ACCOUNTS_ACTIVE_URL,
  type AccountOptionDto,
} from '@/app/features/movements/movements-api';
import { useApi } from '../../lib/use-api';
import { useSettings } from '../../lib/use-settings';
import {
  amountPlaceholder,
  formatAmountForDisplay,
  formatNumberForDisplay,
  parseAmountToNumber,
  sanitizeAmountInput,
  stripThousandSeparators,
  type NumberFormat,
} from '../../lib/amount-format';
import type { GoalType } from './budgets-api';

export type GoalBudgetFormValues = {
  name: string;
  goalType: GoalType;
  currencyId: number | null;
  /** Wire format — JS-number string, period decimal. */
  targetAmount: string;
  startDate: string;
  endDate: string | null;
  description: string | null;
  linkedAccountId: string | null;
};

type Props = {
  mode: 'create' | 'edit';
  goalType: GoalType;
  initialValues: GoalBudgetFormValues;
  onSubmit: (
    values: GoalBudgetFormValues,
  ) => Promise<{ ok: true } | { ok: false; errors: Record<string, string> }>;
  onCancel: () => void;
};

export function GoalBudgetForm({ mode, goalType, initialValues, onSubmit, onCancel }: Props) {
  const [values, setValues] = useState<GoalBudgetFormValues>(initialValues);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitting, setSubmitting] = useState(false);

  const settings = useSettings();
  const numberFormat: NumberFormat | undefined = settings.data?.numberFormat;

  const [displayAmount, setDisplayAmount] = useState('');
  const [amountFocused, setAmountFocused] = useState(false);

  useEffect(() => {
    if (!numberFormat) return;
    if (amountFocused) return;
    const wire = values.targetAmount;
    if (!wire) {
      setDisplayAmount('');
      return;
    }
    const asNumber = Number(wire);
    setDisplayAmount(
      Number.isFinite(asNumber) ? formatNumberForDisplay(asNumber, numberFormat) : '',
    );
  }, [values.targetAmount, numberFormat, amountFocused]);

  const { data: accounts } = useApi<AccountOptionDto[]>(ACCOUNTS_ACTIVE_URL);
  const assetAccounts = useMemo(
    () => (accounts ?? []).filter((a) => a.accountTypeName !== 'Liability'),
    [accounts],
  );

  const { data: currencies } = useApi<{ id: number; code: string; symbol: string }[]>(
    '/api/currencies',
  );

  const currencySymbol = useMemo(() => {
    if (goalType === 'Savings' && values.linkedAccountId) {
      return accounts?.find((a) => a.id === values.linkedAccountId)?.currencySymbol ?? '';
    }
    if (values.currencyId && currencies) {
      return currencies.find((c) => c.id === values.currencyId)?.symbol ?? '';
    }
    return '';
  }, [goalType, values.linkedAccountId, values.currencyId, accounts, currencies]);

  function set<K extends keyof GoalBudgetFormValues>(key: K, value: GoalBudgetFormValues[K]) {
    setValues((prev) => ({ ...prev, [key]: value }));
  }

  function handleAmountChange(input: string) {
    if (!numberFormat) return;
    const sanitized = sanitizeAmountInput(input, numberFormat);
    setDisplayAmount(sanitized);
    const parsed = parseAmountToNumber(sanitized, numberFormat);
    set('targetAmount', Number.isFinite(parsed) ? String(parsed) : '');
  }

  function handleAmountFocus() {
    if (!numberFormat) return;
    setAmountFocused(true);
    setDisplayAmount(stripThousandSeparators(displayAmount, numberFormat));
  }

  function handleAmountBlur() {
    if (!numberFormat) return;
    setAmountFocused(false);
    setDisplayAmount(formatAmountForDisplay(displayAmount, numberFormat));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setErrors({});

    // Client-side end-date check — server enforces too.
    if (values.endDate && values.endDate < values.startDate) {
      setErrors({ endDate: 'End date must be on or after start date.' });
      setSubmitting(false);
      return;
    }

    try {
      const result = await onSubmit(values);
      if (!result.ok) setErrors(result.errors);
    } finally {
      setSubmitting(false);
    }
  }

  const amountInput =
    numberFormat === undefined ? (
      <Skeleton id="gb-amount" className="h-8 w-full" />
    ) : (
      <InputGroup>
        {currencySymbol && (
          <InputGroupAddon align="inline-start">
            <InputGroupText className="text-base font-medium text-muted-foreground">
              {currencySymbol}
            </InputGroupText>
          </InputGroupAddon>
        )}
        <InputGroupInput
          id="gb-amount"
          type="text"
          inputMode="decimal"
          autoComplete="off"
          placeholder={amountPlaceholder(numberFormat)}
          value={displayAmount}
          onChange={(e) => handleAmountChange(e.target.value)}
          onFocus={handleAmountFocus}
          onBlur={handleAmountBlur}
          className="text-base font-medium"
        />
      </InputGroup>
    );

  return (
    <div className="mx-auto max-w-3xl space-y-6">
      {errors._form && (
        <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
          {errors._form}
        </div>
      )}

      <form onSubmit={handleSubmit} className="space-y-5 rounded-lg border border-border bg-card p-4">
        <div className="space-y-1.5">
          <Label htmlFor="gb-name">Name</Label>
          <Input
            id="gb-name"
            value={values.name}
            onChange={(e) => set('name', e.target.value)}
          />
          {errors.name && <p className="text-xs text-destructive">{errors.name}</p>}
        </div>

        {goalType === 'Spending' && (
          <div className="space-y-1.5">
            <Label htmlFor="gb-currency">Currency</Label>
            <CurrencyCombobox value={values.currencyId} onChange={(id) => set('currencyId', id)} />
            {errors.currencyId && <p className="text-xs text-destructive">{errors.currencyId}</p>}
          </div>
        )}

        {goalType === 'Savings' && (
          <div className="space-y-1.5">
            <Label htmlFor="gb-account">Linked account</Label>
            <AccountCombobox
              accounts={assetAccounts}
              value={values.linkedAccountId}
              onChange={(id) => set('linkedAccountId', id)}
              onClear={() => set('linkedAccountId', null)}
              placeholder="Select account"
            />
            {errors.linkedAccountId && <p className="text-xs text-destructive">{errors.linkedAccountId}</p>}
          </div>
        )}

        <div className="space-y-1.5">
          <Label htmlFor="gb-amount">Target</Label>
          {amountInput}
          {errors.targetAmount && <p className="text-xs text-destructive">{errors.targetAmount}</p>}
        </div>

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <div className="space-y-1.5">
            <Label htmlFor="gb-start">Start date</Label>
            <DatePickerField
              id="gb-start"
              hideClear
              value={values.startDate || null}
              onChange={(v) => set('startDate', v ?? '')}
            />
            {errors.startDate && <p className="text-xs text-destructive">{errors.startDate}</p>}
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="gb-end">End date</Label>
            <DatePickerField
              id="gb-end"
              value={values.endDate}
              onChange={(v) => set('endDate', v)}
            />
            {errors.endDate && <p className="text-xs text-destructive">{errors.endDate}</p>}
          </div>
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="gb-description">Description</Label>
          <Textarea
            id="gb-description"
            value={values.description ?? ''}
            onChange={(e) => set('description', e.target.value || null)}
          />
        </div>

        <div className="flex items-center justify-end gap-2 border-t border-border pt-4">
          <Button type="button" variant="outline" onClick={onCancel} disabled={submitting}>Cancel</Button>
          <Button type="submit" disabled={submitting}>{mode === 'create' ? 'Create' : 'Save'}</Button>
        </div>
      </form>
    </div>
  );
}
