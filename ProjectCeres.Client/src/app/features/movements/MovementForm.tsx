import { useEffect, useRef, useState } from 'react';
import { CheckCircle2, Clock } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
  InputGroupText,
} from '@/components/ui/input-group';
import { Label } from '@/components/ui/label';
import { Separator } from '@/components/ui/separator';
import { Skeleton } from '@/components/ui/skeleton';
import { Switch } from '@/components/ui/switch';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog';
import { AccountCombobox } from '../../components/AccountCombobox';
import { CategoryCombobox } from '../../components/CategoryCombobox';
import {
  amountPlaceholder,
  formatAmountForDisplay,
  formatNumberForDisplay,
  parseAmountToNumber,
  sanitizeAmountInput,
  stripThousandSeparators,
  type NumberFormat,
} from '../../lib/amount-format';
import { useSettings } from '../../lib/use-settings';
import type { AccountOptionDto, CategoryOptionDto, MovementType } from './movements-api';

export type MovementFormValues = {
  date: string;
  amount: string; // string in form state; parent converts to Number on submit
  description: string;
  accountId: string | null;
  categoryId: string | null;
  sourceAccountId: string | null;
  destAccountId: string | null;
  assetAccountId: string | null;
  liabilityAccountId: string | null;
  isCleared: boolean;
  // Transaction-only edit-mode fields — carried through silently
  budgetId: string | null;
  needsReview: boolean;
};

export type MovementFormProps = {
  type: MovementType;
  mode: 'create' | 'edit';
  initialValues: MovementFormValues;
  accounts: AccountOptionDto[];
  categories: CategoryOptionDto[];
  onSubmit: (
    values: MovementFormValues,
  ) => Promise<{ ok: true } | { ok: false; errors: Record<string, string> }>;
  onCancel: () => void;
  /** Edit mode only — renders the danger-zone Delete button. */
  onDelete?: () => void;
};

// ---------- helpers ----------

const TYPE_NOUN: Record<MovementType, string> = {
  Transaction: 'transaction',
  Transfer: 'transfer',
  LiabilityPayment: 'liability payment',
};

function formTitle(type: MovementType, mode: 'create' | 'edit'): string {
  const verb = mode === 'create' ? 'New' : 'Edit';
  return `${verb} ${TYPE_NOUN[type]}`;
}

function selectedAccountSymbol(
  type: MovementType,
  values: MovementFormValues,
  accounts: AccountOptionDto[],
): string {
  const id =
    type === 'Transaction'
      ? values.accountId
      : type === 'Transfer'
        ? values.sourceAccountId
        : values.assetAccountId;
  return accounts.find((a) => a.id === id)?.currencySymbol ?? '';
}

// ---------- Field sub-component ----------

function Field({
  label,
  htmlFor,
  error,
  children,
}: {
  label: string;
  htmlFor?: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1.5">
      {htmlFor ? (
        <Label
          htmlFor={htmlFor}
          className="text-sm font-medium tracking-wide text-foreground/80"
        >
          {label}
        </Label>
      ) : (
        <div className="flex items-center gap-2 text-sm font-medium tracking-wide text-foreground/80 select-none">
          {label}
        </div>
      )}
      {children}
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}

// ---------- MovementForm ----------

export function MovementForm({
  type,
  mode,
  initialValues,
  accounts,
  categories,
  onSubmit,
  onCancel,
  onDelete,
}: MovementFormProps) {
  const [values, setValues] = useState<MovementFormValues>(initialValues);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [submitting, setSubmitting] = useState(false);

  const settings = useSettings();
  const numberFormat: NumberFormat | undefined = settings.data?.numberFormat;

  // ── Amount field display state ──
  // values.amount is the *wire* format (JS-number string, period decimal).
  // displayAmount is what the user sees: raw while focused, grouped on blur.
  const [displayAmount, setDisplayAmount] = useState('');
  const [amountFocused, setAmountFocused] = useState(false);

  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => { headingRef.current?.focus(); }, []);

  // Sync when initialValues reference changes (e.g. data loaded by parent)
  useEffect(() => {
    setValues(initialValues);
    setErrors({});
  }, [initialValues]);

  // Whenever the format becomes known or the wire value changes (e.g. Edit
  // load), re-render the display to match the user's locale.
  useEffect(() => {
    if (!numberFormat) return;
    if (amountFocused) return; // don't overwrite mid-edit
    const wire = values.amount;
    if (!wire) {
      setDisplayAmount('');
      return;
    }
    const asNumber = Number(wire);
    setDisplayAmount(
      Number.isFinite(asNumber) ? formatNumberForDisplay(asNumber, numberFormat) : '',
    );
  }, [values.amount, numberFormat, amountFocused]);

  function set<K extends keyof MovementFormValues>(key: K, value: MovementFormValues[K]) {
    setValues((prev) => ({ ...prev, [key]: value }));
  }

  function handleAmountChange(input: string) {
    if (!numberFormat) return;
    const sanitized = sanitizeAmountInput(input, numberFormat);
    setDisplayAmount(sanitized);
    // Mirror to wire format so submit always has the right value.
    const parsed = parseAmountToNumber(sanitized, numberFormat);
    set('amount', Number.isFinite(parsed) ? String(parsed) : '');
  }

  function handleAmountFocus() {
    if (!numberFormat) return;
    setAmountFocused(true);
    // Strip thousands separators so editing is easier.
    setDisplayAmount(stripThousandSeparators(displayAmount, numberFormat));
  }

  function handleAmountBlur() {
    if (!numberFormat) return;
    setAmountFocused(false);
    // Insert thousands separators for display.
    setDisplayAmount(formatAmountForDisplay(displayAmount, numberFormat));
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setErrors({});
    try {
      const result = await onSubmit(values);
      if (!result.ok) {
        setErrors(result.errors);
      }
      // If ok, parent handles navigation/toast/reset
    } finally {
      setSubmitting(false);
    }
  }

  const symbol = selectedAccountSymbol(type, values, accounts);

  // Render the Amount input — or a Skeleton while settings load,
  // since the field's behavior depends on the user's number format.
  const amountInput =
    numberFormat === undefined ? (
      <Skeleton className="h-8 w-full" />
    ) : (
      <InputGroup>
        {symbol && (
          <InputGroupAddon align="inline-start">
            <InputGroupText className="text-base font-medium text-muted-foreground">
              {symbol}
            </InputGroupText>
          </InputGroupAddon>
        )}
        <InputGroupInput
          id="mf-amount"
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

  // Type-specific account fields — extracted so they can render before the
  // Date+Amount row (Account is the prerequisite for the currency symbol).
  const accountFields = (() => {
    if (type === 'Transaction') {
      return (
        <Field label="Account" error={errors.accountId}>
          <AccountCombobox
            accounts={accounts}
            value={values.accountId}
            onChange={(id) => set('accountId', id)}
            placeholder="Select account"
          />
        </Field>
      );
    }
    if (type === 'Transfer') {
      return (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="Source account" error={errors.sourceAccountId}>
            <AccountCombobox
              accounts={accounts}
              value={values.sourceAccountId}
              onChange={(id) => set('sourceAccountId', id)}
              placeholder="Select source"
            />
          </Field>
          <Field label="Destination account" error={errors.destAccountId}>
            <AccountCombobox
              accounts={accounts}
              value={values.destAccountId}
              onChange={(id) => set('destAccountId', id)}
              placeholder="Select destination"
            />
          </Field>
        </div>
      );
    }
    return (
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <Field label="Asset account" error={errors.assetAccountId}>
          <AccountCombobox
            accounts={accounts}
            value={values.assetAccountId}
            onChange={(id) => set('assetAccountId', id)}
            placeholder="Select asset account"
            filter={(a) => a.accountTypeName !== 'Liability'}
          />
        </Field>
        <Field label="Liability account" error={errors.liabilityAccountId}>
          <AccountCombobox
            accounts={accounts}
            value={values.liabilityAccountId}
            onChange={(id) => set('liabilityAccountId', id)}
            placeholder="Select liability account"
            filter={(a) => a.accountTypeName === 'Liability'}
          />
        </Field>
      </div>
    );
  })();

  return (
    <div className="max-w-3xl space-y-6">
      <h1
        ref={headingRef}
        tabIndex={-1}
        className="text-3xl font-semibold outline-none"
      >
        {formTitle(type, mode)}
      </h1>

      {/* _form banner */}
      {errors._form && (
        <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
          {errors._form}
        </div>
      )}

      <form
        onSubmit={handleSubmit}
        className="space-y-5"
        style={{ viewTransitionName: 'movement-form' }}
      >
        {/* ── Account fields first: they unlock the currency symbol on Amount ── */}
        {accountFields}

        {type === 'Transaction' && (
          <Field label="Category" error={errors.categoryId}>
            <CategoryCombobox
              categories={categories}
              value={values.categoryId}
              onChange={(id) => set('categoryId', id)}
              placeholder="Select category"
            />
          </Field>
        )}

        {/* ── Date + Amount ── */}
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="Date" htmlFor="mf-date" error={errors.date}>
            <Input
              id="mf-date"
              type="date"
              value={values.date}
              onChange={(e) => set('date', e.target.value)}
              className="text-base font-medium"
            />
          </Field>

          <Field label="Amount" htmlFor="mf-amount" error={errors.amount}>
            {amountInput}
          </Field>
        </div>

        {/* ── Shared tail fields ── */}
        <Field label="Description" htmlFor="mf-desc" error={errors.description}>
          <Input
            id="mf-desc"
            value={values.description}
            onChange={(e) => set('description', e.target.value)}
          />
        </Field>

        <Separator className="my-2" />

        {/* ── Status row: distinct from data fields, color-shifts on cleared ── */}
        <div
          className={
            'flex items-start justify-between gap-4 rounded-md border p-4 transition-colors duration-200 ' +
            (values.isCleared
              ? 'border-success/30 bg-success/10'
              : 'border-border bg-muted/30')
          }
        >
          <div className="flex items-start gap-3">
            <div
              className={
                'mt-0.5 transition-colors duration-200 ' +
                (values.isCleared ? 'text-success' : 'text-muted-foreground')
              }
              aria-hidden="true"
            >
              {values.isCleared ? (
                <CheckCircle2 className="h-5 w-5" />
              ) : (
                <Clock className="h-5 w-5" />
              )}
            </div>
            <div className="space-y-0.5">
              <div
                className={
                  'text-sm font-medium tracking-wide transition-colors duration-200 ' +
                  (values.isCleared ? 'text-success' : 'text-foreground/80')
                }
              >
                Status
              </div>
              <p
                className={
                  'text-xs transition-colors duration-200 ' +
                  (values.isCleared ? 'text-success/80' : 'text-muted-foreground')
                }
              >
                {values.isCleared
                  ? 'Cleared the bank.'
                  : "Hasn't cleared the bank yet."}
              </p>
            </div>
          </div>
          <Switch
            checked={values.isCleared}
            onCheckedChange={(checked) => set('isCleared', checked)}
            aria-label="Cleared"
          />
        </div>

        {/* ── Footer band ── */}
        <div className="flex items-center justify-between gap-2 border-t border-border pt-4">
          <div>
            {mode === 'edit' && onDelete && (
              <AlertDialog>
                <AlertDialogTrigger
                  render={
                    <Button
                      type="button"
                      variant="ghost"
                      className="text-destructive hover:bg-destructive/10 hover:text-destructive"
                    >
                      Delete
                    </Button>
                  }
                />
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>
                      Delete this {type.toLowerCase()}?
                    </AlertDialogTitle>
                    <AlertDialogDescription>
                      This action cannot be undone.
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>Cancel</AlertDialogCancel>
                    <AlertDialogAction onClick={() => onDelete()}>
                      Delete
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            )}
          </div>

          <div className="flex items-center gap-2">
            <Button type="button" variant="outline" onClick={onCancel}>
              Cancel
            </Button>
            <Button type="submit" disabled={submitting}>
              {submitting ? 'Saving…' : 'Save'}
            </Button>
          </div>
        </div>
      </form>
    </div>
  );
}
