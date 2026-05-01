import { useEffect, useMemo, useState } from 'react';
import { Label } from '@/components/ui/label';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
  InputGroupText,
} from '@/components/ui/input-group';
import { CategoryCombobox } from '@/app/components/CategoryCombobox';
import { CurrencyCombobox } from '@/components/CurrencyCombobox';
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
import {
  CATEGORIES_ACTIVE_URL,
  type CategoryOptionDto,
} from '@/app/features/movements/movements-api';
import {
  CATEGORY_BUDGETS_URL,
  type CategoryBudgetListItemDto,
} from './budgets-api';

export type CategoryBudgetFormValues = {
  categoryId: string | null;
  currencyId: number | null;
  /** Wire format — JS-number string, period decimal. */
  limitAmount: string;
};

type Props = {
  mode: 'create' | 'edit';
  initialValues: CategoryBudgetFormValues;
  /** Currency symbol associated with the chosen currency, derived externally for the InputGroup prefix. */
  currencySymbol: string;
  onCurrencyChange?: (currencyId: number | null) => void;
  onSubmit: (
    values: CategoryBudgetFormValues,
  ) => Promise<{ ok: true } | { ok: false; errors: Record<string, string> }>;
  onCancel: () => void;
};

export function CategoryBudgetForm({
  mode,
  initialValues,
  currencySymbol,
  onCurrencyChange,
  onSubmit,
  onCancel,
}: Props) {
  const [values, setValues] = useState<CategoryBudgetFormValues>(initialValues);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitting, setSubmitting] = useState(false);

  const settings = useSettings();
  const numberFormat: NumberFormat | undefined = settings.data?.numberFormat;

  const [displayAmount, setDisplayAmount] = useState('');
  const [amountFocused, setAmountFocused] = useState(false);

  // Re-render the display amount whenever the format becomes known or the
  // wire value changes (e.g. Edit load).
  useEffect(() => {
    if (!numberFormat) return;
    if (amountFocused) return;
    const wire = values.limitAmount;
    if (!wire) {
      setDisplayAmount('');
      return;
    }
    const asNumber = Number(wire);
    setDisplayAmount(
      Number.isFinite(asNumber) ? formatNumberForDisplay(asNumber, numberFormat) : '',
    );
  }, [values.limitAmount, numberFormat, amountFocused]);

  // Categories — only Expense.
  const { data: allCategories } = useApi<CategoryOptionDto[]>(CATEGORIES_ACTIVE_URL);
  const expenseCategories = useMemo(
    () => (allCategories ?? []).filter((c) => c.categoryTypeName === 'Expense'),
    [allCategories],
  );

  // Already-budgeted categories (any currency, active or archived) — used to
  // decorate the picker with a "(budgeted)" hint so the user doesn't try to
  // create a duplicate. The server enforces uniqueness; this is a UX nudge.
  const { data: existingBudgets } = useApi<CategoryBudgetListItemDto[]>(
    `${CATEGORY_BUDGETS_URL}?includeArchived=true`,
  );

  // Pull currencies to map currencyId → currencyCode.
  const { data: currencies } = useApi<{ id: number; code: string; symbol: string }[]>(
    '/api/currencies',
  );
  const currencyCode = useMemo(() => {
    if (!values.currencyId || !currencies) return null;
    return currencies.find((c) => c.id === values.currencyId)?.code ?? null;
  }, [values.currencyId, currencies]);

  const budgetedKeys = useMemo(() => {
    const set = new Set<string>();
    if (!existingBudgets) return set;
    for (const b of existingBudgets) {
      // Skip the budget being edited so its own category isn't flagged.
      if (mode === 'edit' && b.categoryId === values.categoryId) continue;
      set.add(`${b.categoryId}|${b.currencyCode}`);
    }
    return set;
  }, [existingBudgets, mode, values.categoryId]);

  const finalCategories = useMemo(() => {
    if (!currencyCode) return expenseCategories;
    return expenseCategories.map((c) => {
      const isBudgeted = budgetedKeys.has(`${c.id}|${currencyCode}`);
      return isBudgeted ? { ...c, name: `${c.name} (budgeted)` } : c;
    });
  }, [expenseCategories, budgetedKeys, currencyCode]);

  function set<K extends keyof CategoryBudgetFormValues>(key: K, value: CategoryBudgetFormValues[K]) {
    setValues((prev) => ({ ...prev, [key]: value }));
    if (key === 'currencyId') {
      onCurrencyChange?.(value as number | null);
    }
  }

  function handleAmountChange(input: string) {
    if (!numberFormat) return;
    const sanitized = sanitizeAmountInput(input, numberFormat);
    setDisplayAmount(sanitized);
    const parsed = parseAmountToNumber(sanitized, numberFormat);
    set('limitAmount', Number.isFinite(parsed) ? String(parsed) : '');
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
    try {
      const result = await onSubmit(values);
      if (!result.ok) setErrors(result.errors);
    } finally {
      setSubmitting(false);
    }
  }

  const amountInput =
    numberFormat === undefined ? (
      <Skeleton id="cb-amount" className="h-8 w-full" />
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
          id="cb-amount"
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
          <Label htmlFor="cb-category">Category</Label>
          <CategoryCombobox
            categories={finalCategories}
            value={values.categoryId}
            onChange={(id) => set('categoryId', id)}
            placeholder="Select category"
          />
          {errors.categoryId && <p className="text-xs text-destructive">{errors.categoryId}</p>}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="cb-currency">Currency</Label>
          <CurrencyCombobox
            value={values.currencyId}
            onChange={(id) => set('currencyId', id)}
          />
          {errors.currencyId && <p className="text-xs text-destructive">{errors.currencyId}</p>}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="cb-amount">Limit</Label>
          {amountInput}
          {errors.limitAmount && <p className="text-xs text-destructive">{errors.limitAmount}</p>}
        </div>

        <div className="flex items-center justify-end gap-2 border-t border-border pt-4">
          <Button type="button" variant="outline" onClick={onCancel} disabled={submitting}>Cancel</Button>
          <Button type="submit" disabled={submitting}>{mode === 'create' ? 'Create' : 'Save'}</Button>
        </div>
      </form>
    </div>
  );
}
