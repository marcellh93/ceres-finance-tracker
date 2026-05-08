import { useEffect, useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { AccountCombobox } from './AccountCombobox';
import { CategoryCombobox } from './CategoryCombobox';
import { Field } from './Field';
import { MoneyInput } from './MoneyInput';
import { SubmitButton } from './SubmitButton';
import { DatePickerField } from '../../components/DatePickerField';
import {
  ACCOUNTS_ACTIVE_URL,
  CATEGORIES_ACTIVE_URL,
  LIABILITY_PAYMENTS_CREATE_URL,
  TRANSACTIONS_CREATE_URL,
  TRANSFERS_CREATE_URL,
  type AccountOptionDto,
  type CategoryOptionDto,
} from '../features/movements/movements-api';

type Props = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSaved?: () => void;
};

type TabKey = 'transaction' | 'transfer' | 'liabilityPayment';
type FieldErrors = Record<string, string>;

const todayIso = () => new Date().toISOString().slice(0, 10);

export function QuickAddModal({ open, onOpenChange, onSaved }: Props) {
  const [tab, setTab] = useState<TabKey>('transaction');
  const [accounts, setAccounts] = useState<AccountOptionDto[]>([]);
  const [categories, setCategories] = useState<CategoryOptionDto[]>([]);
  const [errors, setErrors] = useState<FieldErrors>({});

  // Shared fields
  const [date, setDate] = useState(todayIso());
  const [amount, setAmount] = useState('');
  const [description, setDescription] = useState('');

  // Transaction-specific
  const [accountId, setAccountId] = useState<string | null>(null);
  const [categoryId, setCategoryId] = useState<string | null>(null);

  // Transfer-specific
  const [sourceAccountId, setSourceAccountId] = useState<string | null>(null);
  const [destAccountId, setDestAccountId] = useState<string | null>(null);

  // Liability Payment-specific
  const [assetAccountId, setAssetAccountId] = useState<string | null>(null);
  const [liabilityAccountId, setLiabilityAccountId] = useState<string | null>(null);

  // Load comboboxes when the modal opens
  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    Promise.all([
      fetch(ACCOUNTS_ACTIVE_URL, { signal: controller.signal }).then((r) => r.json()),
      fetch(CATEGORIES_ACTIVE_URL, { signal: controller.signal }).then((r) => r.json()),
    ])
      .then(([accs, cats]) => {
        setAccounts(accs);
        setCategories(cats);
      })
      .catch((e) => {
        if (controller.signal.aborted) return;
        toast.error('Could not load accounts or categories.');
        // eslint-disable-next-line no-console
        console.error(e);
      });
    return () => controller.abort();
  }, [open]);

  function resetForm() {
    setDate(todayIso());
    setAmount('');
    setDescription('');
    setAccountId(null);
    setCategoryId(null);
    setSourceAccountId(null);
    setDestAccountId(null);
    setAssetAccountId(null);
    setLiabilityAccountId(null);
    setErrors({});
  }

  function selectedAccountSymbol(): string {
    const id =
      tab === 'transaction'
        ? accountId
        : tab === 'transfer'
          ? sourceAccountId
          : assetAccountId;
    return accounts.find((a) => a.id === id)?.currencySymbol ?? '';
  }

  function handleTabChange(value: unknown) {
    setTab(value as TabKey);
    setErrors({});
    // Reset per-tab fields so a stale selection from another tab can't leak in.
    // Shared fields (date, amount, description) intentionally persist.
    setAccountId(null);
    setCategoryId(null);
    setSourceAccountId(null);
    setDestAccountId(null);
    setAssetAccountId(null);
    setLiabilityAccountId(null);
  }

  async function submit(): Promise<boolean> {
    setErrors({});
    try {
      const url =
        tab === 'transaction' ? TRANSACTIONS_CREATE_URL :
        tab === 'transfer' ? TRANSFERS_CREATE_URL :
        LIABILITY_PAYMENTS_CREATE_URL;

      const body =
        tab === 'transaction'
          ? { date, amount: Number(amount), accountId, categoryId, description: description || null }
          : tab === 'transfer'
            ? { date, amount: Number(amount), sourceAccountId, destAccountId, description: description || null }
            : { date, amount: Number(amount), assetAccountId, liabilityAccountId, description: description || null };

      const response = await fetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });

      if (response.ok) {
        toast.success('Saved.');
        resetForm();
        onSaved?.();
        return true;
      }

      if (response.status === 422) {
        const problem = await response.json();
        const flat: FieldErrors = {};
        if (problem?.errors && typeof problem.errors === 'object') {
          for (const [key, messages] of Object.entries(problem.errors as Record<string, string[]>)) {
            // ASP.NET sends PascalCase keys; lowercase the first letter for matching
            const camelKey = key.charAt(0).toLowerCase() + key.slice(1);
            flat[camelKey] = messages.join(' ');
          }
        }
        setErrors(flat);
        throw new Error('validation');
      }

      toast.error("Couldn't save. Try again.");
      throw new Error('server');
    } catch (err) {
      if (err instanceof Error && (err.message === 'validation' || err.message === 'server')) {
        throw err;
      }
      toast.error("Couldn't save. Try again.");
      throw new Error('server');
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Quick add</DialogTitle>
        </DialogHeader>

        <Tabs value={tab} onValueChange={handleTabChange}>
          <TabsList className="grid w-full grid-cols-3">
            <TabsTrigger value="transaction">Transaction</TabsTrigger>
            <TabsTrigger value="transfer">Transfer</TabsTrigger>
            <TabsTrigger value="liabilityPayment">Debt Payment</TabsTrigger>
          </TabsList>

          <TabsContent value="transaction" className="space-y-3 pt-3">
            <Field label="Date" htmlFor="qa-date" error={errors.date}>
              <DatePickerField id="qa-date" value={date || null} onChange={(v) => setDate(v ?? '')} hideClear />
            </Field>
            <Field label="Amount" htmlFor="qa-amount" error={errors.amount}>
              <MoneyInput
                id="qa-amount"
                value={amount}
                onChange={setAmount}
                currencySymbol={selectedAccountSymbol() || null}
              />
            </Field>
            <Field label="Account" error={errors.accountId}>
              <AccountCombobox accounts={accounts} value={accountId} onChange={setAccountId} onClear={() => setAccountId(null)} placeholder="Select account" />
            </Field>
            <Field label="Category" error={errors.categoryId}>
              <CategoryCombobox categories={categories} value={categoryId} onChange={setCategoryId} onClear={() => setCategoryId(null)} placeholder="Select category" />
            </Field>
            <Field label="Description" htmlFor="qa-desc" error={errors.description}>
              <Input id="qa-desc" value={description} onChange={(e) => setDescription(e.target.value)} />
            </Field>
          </TabsContent>

          <TabsContent value="transfer" className="space-y-3 pt-3">
            <Field label="Date" htmlFor="qa-tr-date" error={errors.date}>
              <DatePickerField id="qa-tr-date" value={date || null} onChange={(v) => setDate(v ?? '')} hideClear />
            </Field>
            <Field label="Amount" htmlFor="qa-tr-amount" error={errors.amount}>
              <MoneyInput
                id="qa-tr-amount"
                value={amount}
                onChange={setAmount}
                currencySymbol={selectedAccountSymbol() || null}
              />
            </Field>
            <Field label="Source account" error={errors.sourceAccountId}>
              <AccountCombobox accounts={accounts} value={sourceAccountId} onChange={setSourceAccountId} onClear={() => setSourceAccountId(null)} placeholder="Select source" />
            </Field>
            <Field label="Destination account" error={errors.destAccountId}>
              <AccountCombobox accounts={accounts} value={destAccountId} onChange={setDestAccountId} onClear={() => setDestAccountId(null)} placeholder="Select destination" />
            </Field>
            <Field label="Description" htmlFor="qa-tr-desc" error={errors.description}>
              <Input id="qa-tr-desc" value={description} onChange={(e) => setDescription(e.target.value)} />
            </Field>
          </TabsContent>

          <TabsContent value="liabilityPayment" className="space-y-3 pt-3">
            <Field label="Date" htmlFor="qa-lp-date" error={errors.date}>
              <DatePickerField id="qa-lp-date" value={date || null} onChange={(v) => setDate(v ?? '')} hideClear />
            </Field>
            <Field label="Amount" htmlFor="qa-lp-amount" error={errors.amount}>
              <MoneyInput
                id="qa-lp-amount"
                value={amount}
                onChange={setAmount}
                currencySymbol={selectedAccountSymbol() || null}
              />
            </Field>
            <Field label="Asset account" error={errors.assetAccountId}>
              <AccountCombobox
                accounts={accounts}
                value={assetAccountId}
                onChange={setAssetAccountId}
                onClear={() => setAssetAccountId(null)}
                placeholder="Select asset account"
                filter={(a) => a.accountTypeName !== 'Liability'}
              />
            </Field>
            <Field label="Liability account" error={errors.liabilityAccountId}>
              <AccountCombobox
                accounts={accounts}
                value={liabilityAccountId}
                onChange={setLiabilityAccountId}
                onClear={() => setLiabilityAccountId(null)}
                placeholder="Select liability account"
                filter={(a) => a.accountTypeName === 'Liability'}
              />
            </Field>
            <Field label="Description" htmlFor="qa-lp-desc" error={errors.description}>
              <Input id="qa-lp-desc" value={description} onChange={(e) => setDescription(e.target.value)} />
            </Field>
          </TabsContent>
        </Tabs>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>Cancel</Button>
          <SubmitButton
            onClick={async () => {
              await submit();
              onOpenChange(false);
            }}
          >
            Save
          </SubmitButton>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

