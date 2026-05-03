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
import { Switch } from '@/components/ui/switch';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import { cn } from '@/lib/utils';
import type {
  AccountFormValues,
  AccountTypeDto,
  CurrencyDto,
  CreateAccountRequest,
  RepaymentType,
  UpdateAccountRequest,
} from './accounts-api';

type SubmitResult = { ok: true } | { ok: false };

type Props = {
  mode: 'create' | 'edit';
  initialValues: AccountFormValues;
  accountTypes: AccountTypeDto[];
  currencies: CurrencyDto[];
  onSubmit: (
    values: CreateAccountRequest | UpdateAccountRequest,
  ) => Promise<SubmitResult>;
  onCancel: () => void;
};

const REPAYMENT_OPTIONS: { value: RepaymentType | null; label: string }[] = [
  { value: null,         label: '— None —'     },
  { value: 'FullMonthly', label: 'Full Monthly' },
  { value: 'Amortising',  label: 'Amortising'   },
];

const TYPE_LOCKED_TOOLTIP =
  'Account type cannot be changed after creation. Create a new account if you need a different type.';
const CURRENCY_LOCKED_TOOLTIP =
  'Account currency cannot be changed after creation. Create a new account if you need a different currency.';

function shallowEqual(a: AccountFormValues, b: AccountFormValues): boolean {
  return (
    a.name                   === b.name                   &&
    a.accountTypeId          === b.accountTypeId          &&
    a.currencyId             === b.currencyId             &&
    a.description            === b.description            &&
    a.openingBalance         === b.openingBalance         &&
    a.openingBalanceDate     === b.openingBalanceDate     &&
    a.liabilityRepaymentType === b.liabilityRepaymentType &&
    a.interestRate           === b.interestRate           &&
    a.excludeFromSpendable   === b.excludeFromSpendable
  );
}

export function AccountForm({
  mode, initialValues, accountTypes, currencies, onSubmit, onCancel,
}: Props) {
  const normalised: AccountFormValues = {
    ...initialValues,
    description: initialValues.description ?? '',
  };
  const [snapshot, setSnapshot] = useState<AccountFormValues>(normalised);
  const [values, setValues] = useState<AccountFormValues>(normalised);
  const [submitting, setSubmitting] = useState(false);
  const [advancedOpen, setAdvancedOpen] = useState(false);

  const isDirty = !shallowEqual(values, snapshot);

  const isLiability = values.accountTypeId === 2;
  const isAmortising = values.liabilityRepaymentType === 'Amortising';

  const selectedType = accountTypes.find((t) => t.id === values.accountTypeId);
  const selectedCurrency = currencies.find((c) => c.id === values.currencyId);

  async function handleSubmit(e: React.FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!isDirty || submitting) return;
    setSubmitting(true);

    // Layer 1: SPA submit-time normalisation. Always ensure the wire is self-consistent.
    const normalisedRepayment = isLiability ? values.liabilityRepaymentType : null;
    const normalisedRate = isLiability && normalisedRepayment === 'Amortising'
      ? values.interestRate
      : null;
    const normalisedExclude = isLiability ? false : values.excludeFromSpendable;

    const body =
      mode === 'create'
        ? ({
            name: values.name.trim(),
            accountTypeId: values.accountTypeId,
            currencyId: values.currencyId,
            description: values.description.trim() === '' ? null : values.description.trim(),
            openingBalance: values.openingBalance,
            openingBalanceDate: values.openingBalanceDate,
            liabilityRepaymentType: normalisedRepayment,
            interestRate: normalisedRate,
            excludeFromSpendable: normalisedExclude,
          } as CreateAccountRequest)
        : ({
            name: values.name.trim(),
            description: values.description.trim() === '' ? null : values.description.trim(),
            openingBalance: values.openingBalance,
            openingBalanceDate: values.openingBalanceDate,
            liabilityRepaymentType: normalisedRepayment,
            interestRate: normalisedRate,
            excludeFromSpendable: normalisedExclude,
          } as UpdateAccountRequest);

    const result = await onSubmit(body);
    setSubmitting(false);
    if (result.ok) setSnapshot(values);
  }

  return (
    <TooltipProvider delay={200}>
      <form onSubmit={handleSubmit} className="space-y-6">
        <div className="space-y-1.5">
          <Label htmlFor="accountName">Name *</Label>
          <Input
            id="accountName"
            aria-label="Name"
            value={values.name}
            onChange={(e) => setValues((v) => ({ ...v, name: e.target.value }))}
            required
            maxLength={100}
          />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="accountType">{mode === 'create' ? 'Type *' : 'Type'}</Label>
          {mode === 'edit' ? (
            <LockedRow
              id="accountType"
              ariaLabel="Type"
              text={selectedType?.name ?? '—'}
              tooltip={TYPE_LOCKED_TOOLTIP}
            />
          ) : (
            <TypeCombobox
              id="accountType"
              value={values.accountTypeId}
              types={accountTypes}
              onChange={(id) =>
                setValues((v) => ({
                  ...v,
                  accountTypeId: id,
                  liabilityRepaymentType: id === 2 ? v.liabilityRepaymentType : null,
                  excludeFromSpendable: id === 1 ? v.excludeFromSpendable : false,
                }))
              }
            />
          )}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="accountCurrency">{mode === 'create' ? 'Currency *' : 'Currency'}</Label>
          {mode === 'edit' ? (
            <LockedRow
              id="accountCurrency"
              ariaLabel="Currency"
              text={selectedCurrency ? `${selectedCurrency.code} — ${selectedCurrency.name} ${selectedCurrency.symbol}` : '—'}
              tooltip={CURRENCY_LOCKED_TOOLTIP}
            />
          ) : (
            <CurrencyCombobox
              id="accountCurrency"
              value={values.currencyId}
              currencies={currencies}
              onChange={(id) => setValues((v) => ({ ...v, currencyId: id }))}
            />
          )}
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="accountDescription">Description</Label>
          <Input
            id="accountDescription"
            aria-label="Description"
            value={values.description}
            onChange={(e) => setValues((v) => ({ ...v, description: e.target.value }))}
            maxLength={500}
          />
        </div>

        {!isLiability ? (
          <div className="space-y-1.5">
            <div className="flex items-center gap-2">
              <Switch
                id="excludeFromSpendable"
                checked={values.excludeFromSpendable}
                onCheckedChange={(checked) =>
                  setValues((v) => ({ ...v, excludeFromSpendable: checked }))
                }
              />
              <Label htmlFor="excludeFromSpendable" className="text-sm font-normal">
                Exclude from spendable balance
              </Label>
            </div>
            <p className="text-xs text-muted-foreground">
              Excluded accounts don't count toward your spendable balance but still appear in net worth.
            </p>
          </div>
        ) : (
          <>
            <div className="space-y-1.5">
              <Label htmlFor="repaymentType">Repayment type</Label>
              <RepaymentCombobox
                id="repaymentType"
                value={values.liabilityRepaymentType}
                onChange={(rt) => setValues((v) => ({ ...v, liabilityRepaymentType: rt }))}
              />
              <p className="text-xs text-muted-foreground">
                <strong>Full Monthly</strong> — pay the full balance each month; no interest accrues.{' '}
                <strong>Amortising</strong> — fixed monthly payment with interest (loans, mortgages).
              </p>
            </div>
            {isAmortising ? (
              <div className="space-y-1.5">
                <Label htmlFor="interestRate">Interest rate</Label>
                <Input
                  id="interestRate"
                  aria-label="Interest rate"
                  type="number"
                  step="0.0001"
                  min={0}
                  max={1}
                  value={values.interestRate ?? ''}
                  onChange={(e) =>
                    setValues((v) => ({
                      ...v,
                      interestRate: e.target.value === '' ? null : Number(e.target.value),
                    }))
                  }
                />
                <p className="text-xs text-muted-foreground">
                  Annual interest rate as a decimal. e.g. 0.035 = 3.5%.
                </p>
              </div>
            ) : null}
          </>
        )}

        {mode === 'create' ? (
          <OpeningBalanceFields values={values} setValues={setValues} />
        ) : (
          <div className="rounded-md border border-input p-3">
            <button
              type="button"
              className="w-full text-left cursor-pointer text-sm font-medium select-none"
              onClick={() => setAdvancedOpen((o) => !o)}
              aria-expanded={advancedOpen}
            >
              {advancedOpen ? '▼' : '▶'} Advanced — opening balance and start date
            </button>
            {advancedOpen ? (
              <div className="mt-4">
                <OpeningBalanceFields values={values} setValues={setValues} editing />
              </div>
            ) : null}
          </div>
        )}

        <div className="flex items-center gap-2 pt-2">
          <Button type="submit" disabled={!isDirty || submitting}>
            {submitting ? 'Saving…' : 'Save'}
          </Button>
          <Button type="button" variant="outline" onClick={onCancel} disabled={submitting}>
            Cancel
          </Button>
        </div>
      </form>
    </TooltipProvider>
  );
}

function LockedRow({
  id, ariaLabel, text, tooltip,
}: { id: string; ariaLabel: string; text: string; tooltip: string }) {
  return (
    <div
      id={id}
      aria-label={ariaLabel}
      className="flex h-9 w-full items-center justify-between rounded-md border border-input bg-muted/40 px-3 text-sm"
    >
      <span>{text}</span>
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
        <TooltipContent className="max-w-xs">{tooltip}</TooltipContent>
      </Tooltip>
    </div>
  );
}

function OpeningBalanceFields({
  values, setValues, editing,
}: {
  values: AccountFormValues;
  setValues: React.Dispatch<React.SetStateAction<AccountFormValues>>;
  editing?: boolean;
}) {
  return (
    <div className="space-y-4">
      <div className="space-y-1.5">
        <Label htmlFor="openingBalance">Opening balance</Label>
        <Input
          id="openingBalance"
          aria-label="Opening balance"
          type="number"
          step="0.01"
          value={values.openingBalance}
          onChange={(e) =>
            setValues((v) => ({ ...v, openingBalance: Number(e.target.value) }))
          }
        />
        <p className="text-xs text-muted-foreground">
          Leave at 0 to start with no balance. A negative value records a liability opening balance.
        </p>
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="openingBalanceDate">Start date</Label>
        <Input
          id="openingBalanceDate"
          aria-label="Start date"
          type="date"
          value={values.openingBalanceDate}
          onChange={(e) => setValues((v) => ({ ...v, openingBalanceDate: e.target.value }))}
        />
        <p className="text-xs text-muted-foreground">
          {editing
            ? 'Moving the start date backward lets you record earlier transactions. Moving it forward will hide existing transactions before that date from the active list — they remain in the database but become read-only.'
            : 'The date this account starts being tracked. Defaults to today.'}
        </p>
      </div>
    </div>
  );
}

function TypeCombobox({
  id, value, types, onChange,
}: {
  id: string;
  value: number;
  types: AccountTypeDto[];
  onChange: (id: number) => void;
}) {
  const [open, setOpen] = useState(false);
  const selected = types.find((t) => t.id === value);
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            id={id} type="button" variant="outline" role="combobox"
            aria-label="Type" aria-expanded={open}
            className="w-full justify-between"
          >
            <span>{selected?.name ?? 'Select type'}</span>
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent className="p-0 w-(--anchor-width) min-w-(--anchor-width)" align="start">
        <Command>
          <CommandList>
            <CommandGroup>
              {types.map((t) => (
                <CommandItem key={t.id} value={t.name} className="whitespace-nowrap"
                  onSelect={() => { onChange(t.id); setOpen(false); }}>
                  <Check className={cn('mr-2 h-4 w-4', t.id === value ? 'opacity-100' : 'opacity-0')} />
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

function CurrencyCombobox({
  id, value, currencies, onChange,
}: {
  id: string;
  value: number;
  currencies: CurrencyDto[];
  onChange: (id: number) => void;
}) {
  const [open, setOpen] = useState(false);
  const selected = currencies.find((c) => c.id === value);
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            id={id} type="button" variant="outline" role="combobox"
            aria-label="Currency" aria-expanded={open}
            className="w-full justify-between"
          >
            <span>{selected ? `${selected.code} — ${selected.name} ${selected.symbol}` : 'Select currency'}</span>
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent className="p-0 w-(--anchor-width) min-w-(--anchor-width)" align="start">
        <Command>
          <CommandList>
            <CommandGroup>
              {currencies.map((c) => (
                <CommandItem key={c.id} value={c.code} className="whitespace-nowrap"
                  onSelect={() => { onChange(c.id); setOpen(false); }}>
                  <Check className={cn('mr-2 h-4 w-4', c.id === value ? 'opacity-100' : 'opacity-0')} />
                  {c.code} — {c.name} {c.symbol}
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}

function RepaymentCombobox({
  id, value, onChange,
}: {
  id: string;
  value: RepaymentType | null;
  onChange: (v: RepaymentType | null) => void;
}) {
  const [open, setOpen] = useState(false);
  const selected = REPAYMENT_OPTIONS.find((o) => o.value === value);
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        render={
          <Button
            id={id} type="button" variant="outline" role="combobox"
            aria-label="Repayment type" aria-expanded={open}
            className="w-full justify-between"
          >
            <span>{selected?.label ?? '— None —'}</span>
            <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
          </Button>
        }
      />
      <PopoverContent className="p-0 w-(--anchor-width) min-w-(--anchor-width)" align="start">
        <Command>
          <CommandList>
            <CommandGroup>
              {REPAYMENT_OPTIONS.map((opt) => (
                <CommandItem key={opt.label} value={opt.label} className="whitespace-nowrap"
                  onSelect={() => { onChange(opt.value); setOpen(false); }}>
                  <Check className={cn('mr-2 h-4 w-4', opt.value === value ? 'opacity-100' : 'opacity-0')} />
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
