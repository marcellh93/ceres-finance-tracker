import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
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
        <Label htmlFor={htmlFor}>{label}</Label>
      ) : (
        <div className="flex items-center gap-2 text-sm leading-none font-medium select-none">
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

  // Sync when initialValues reference changes (e.g. data loaded by parent)
  useEffect(() => {
    setValues(initialValues);
    setErrors({});
  }, [initialValues]);

  function set<K extends keyof MovementFormValues>(key: K, value: MovementFormValues[K]) {
    setValues((prev) => ({ ...prev, [key]: value }));
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

  return (
    <div className="space-y-4">
      {/* _form banner */}
      {errors._form && (
        <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
          {errors._form}
        </div>
      )}

      <form onSubmit={handleSubmit} className="space-y-4">
        {/* ── Shared fields ── */}
        <Field label="Date" htmlFor="mf-date" error={errors.date}>
          <Input
            id="mf-date"
            type="date"
            value={values.date}
            onChange={(e) => set('date', e.target.value)}
          />
        </Field>

        <Field label="Amount" htmlFor="mf-amount" error={errors.amount}>
          <div className="flex items-center gap-2">
            <span className="text-sm text-muted-foreground w-6">{symbol}</span>
            <Input
              id="mf-amount"
              type="number"
              step="0.01"
              value={values.amount}
              onChange={(e) => set('amount', e.target.value)}
            />
          </div>
        </Field>

        {/* ── Type-specific fields ── */}
        {type === 'Transaction' && (
          <>
            <Field label="Account" error={errors.accountId}>
              <AccountCombobox
                accounts={accounts}
                value={values.accountId}
                onChange={(id) => set('accountId', id)}
                placeholder="Select account"
              />
            </Field>
            <Field label="Category" error={errors.categoryId}>
              <CategoryCombobox
                categories={categories}
                value={values.categoryId}
                onChange={(id) => set('categoryId', id)}
                placeholder="Select category"
              />
            </Field>
          </>
        )}

        {type === 'Transfer' && (
          <>
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
          </>
        )}

        {type === 'LiabilityPayment' && (
          <>
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
          </>
        )}

        {/* ── Shared tail fields ── */}
        <Field label="Description" htmlFor="mf-desc" error={errors.description}>
          <Input
            id="mf-desc"
            value={values.description}
            onChange={(e) => set('description', e.target.value)}
          />
        </Field>

        <Field label="Cleared">
          <div className="flex items-center gap-3">
            <Switch
              checked={values.isCleared}
              onCheckedChange={(checked) => set('isCleared', checked)}
              aria-label="Cleared"
            />
            <span className="text-sm text-foreground">
              {values.isCleared ? 'Cleared' : 'Uncleared'}
            </span>
          </div>
        </Field>

        {/* ── Footer ── */}
        <div className="flex items-center justify-between pt-2">
          {/* Danger zone: Delete in edit mode */}
          <div>
            {mode === 'edit' && onDelete && (
              <AlertDialog>
                <AlertDialogTrigger
                  render={<Button type="button" variant="destructive">Delete</Button>}
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

          {/* Primary actions */}
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
