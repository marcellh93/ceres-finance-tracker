import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { AccountForm } from './AccountForm';
import type { AccountFormValues, AccountTypeDto, CurrencyDto } from './accounts-api';

const accountTypes: AccountTypeDto[] = [
  { id: 1, name: 'Asset' },
  { id: 2, name: 'Liability' },
];

const currencies: CurrencyDto[] = [
  { id: 1, code: 'EUR', name: 'Euro',     symbol: '€' },
  { id: 2, code: 'USD', name: 'US Dollar', symbol: '$' },
];

const initialCreate: AccountFormValues = {
  name: '',
  accountTypeId: 1,
  currencyId: 1,
  description: '',
  openingBalance: 0,
  openingBalanceDate: '2026-05-03',
  liabilityRepaymentType: null,
  interestRate: null,
  excludeFromSpendable: false,
};

const initialEditAsset: AccountFormValues = {
  name: 'Checking Account',
  accountTypeId: 1,
  currencyId: 1,
  description: 'Main checking',
  openingBalance: 1000,
  openingBalanceDate: '2026-01-01',
  liabilityRepaymentType: null,
  interestRate: null,
  excludeFromSpendable: false,
};

const initialEditLiability: AccountFormValues = {
  name: 'Mortgage',
  accountTypeId: 2,
  currencyId: 1,
  description: '',
  openingBalance: 100000,
  openingBalanceDate: '2026-01-01',
  liabilityRepaymentType: 'Amortising',
  interestRate: 0.035,
  excludeFromSpendable: false,
};

function renderForm(overrides?: {
  mode?: 'create' | 'edit';
  initialValues?: AccountFormValues;
  onSubmit?: ReturnType<typeof vi.fn>;
}) {
  const mode = overrides?.mode ?? 'create';
  const initialValues =
    overrides?.initialValues ??
    (mode === 'create' ? initialCreate : initialEditAsset);
  const onSubmit = overrides?.onSubmit ?? vi.fn().mockResolvedValue({ ok: true });
  const onCancel = vi.fn();
  const utils = render(
    <AccountForm
      mode={mode}
      initialValues={initialValues}
      accountTypes={accountTypes}
      currencies={currencies}
      onSubmit={onSubmit}
      onCancel={onCancel}
    />,
  );
  return { ...utils, onSubmit, onCancel };
}

describe('AccountForm', () => {
  it('renders Name, Type, Currency, Description with initial values (Edit)', () => {
    renderForm({ mode: 'edit' });
    expect(screen.getByLabelText(/name/i)).toHaveValue('Checking Account');
    expect(within(screen.getByLabelText(/^type$/i)).getByText('Asset')).toBeInTheDocument();
    expect(within(screen.getByLabelText(/currency/i)).getByText(/EUR/)).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toHaveValue('Main checking');
  });

  it('Type picker is editable on Create', () => {
    renderForm({ mode: 'create' });
    expect(screen.getByLabelText(/^type$/i)).toHaveAttribute('aria-expanded');
  });

  it('Type is read-only on Edit', () => {
    renderForm({ mode: 'edit' });
    const trigger = screen.getByLabelText(/^type$/i);
    expect(trigger).not.toHaveAttribute('aria-expanded');
  });

  it('Currency is read-only on Edit', () => {
    renderForm({ mode: 'edit' });
    const trigger = screen.getByLabelText(/currency/i);
    expect(trigger).not.toHaveAttribute('aria-expanded');
  });

  it('Asset block (Exclude from spendable) renders when Type is Asset', () => {
    renderForm({ mode: 'edit', initialValues: initialEditAsset });
    expect(screen.getByText(/exclude from spendable/i)).toBeInTheDocument();
    expect(screen.queryByText(/repayment type/i)).toBeNull();
  });

  it('Liability block (Repayment type) renders when Type is Liability', () => {
    renderForm({ mode: 'edit', initialValues: initialEditLiability });
    expect(screen.getByText(/repayment type/i)).toBeInTheDocument();
    expect(screen.queryByText(/exclude from spendable/i)).toBeNull();
  });

  it('Interest rate field renders when Liability + Amortising', () => {
    renderForm({ mode: 'edit', initialValues: initialEditLiability });
    expect(screen.getByLabelText(/interest rate/i)).toBeInTheDocument();
  });

  it('Save and Cancel are visible', () => {
    renderForm({ mode: 'edit' });
    expect(screen.getByRole('button', { name: /save/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /cancel/i })).toBeInTheDocument();
  });

  it('Cancel calls onCancel', () => {
    const { onCancel } = renderForm({ mode: 'edit' });
    fireEvent.click(screen.getByRole('button', { name: /cancel/i }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('Save is disabled when not dirty (Edit)', () => {
    renderForm({ mode: 'edit' });
    expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
  });

  it('Save enables when Name changes', () => {
    renderForm({ mode: 'edit' });
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Renamed' } });
    expect(screen.getByRole('button', { name: /save/i })).toBeEnabled();
  });

  it('Save shows "Saving…" while pending', () => {
    let resolveSubmit: (v: { ok: true }) => void = () => {};
    const onSubmit = vi.fn(
      () => new Promise<{ ok: true }>((resolve) => { resolveSubmit = resolve; }),
    );
    renderForm({ mode: 'edit', onSubmit });
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Renamed' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    expect(screen.getByRole('button', { name: /saving/i })).toBeDisabled();
    resolveSubmit({ ok: true });
  });

  it('Inputs retain edits when onSubmit returns ok:false', async () => {
    const onSubmit = vi.fn().mockResolvedValue({ ok: false });
    renderForm({ mode: 'edit', onSubmit });
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Renamed' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /save/i })).toBeEnabled(),
    );
    expect(screen.getByLabelText(/name/i)).toHaveValue('Renamed');
  });

  it('Submit normalises interestRate to null when repayment type is not Amortising', async () => {
    const onSubmit = vi.fn().mockResolvedValue({ ok: true });
    renderForm({ mode: 'edit', initialValues: initialEditLiability, onSubmit });
    fireEvent.click(screen.getByLabelText(/repayment type/i));
    fireEvent.click(await screen.findByRole('option', { name: /Full Monthly/i }));
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalled());
    const submittedValues = onSubmit.mock.calls[0][0];
    expect(submittedValues.liabilityRepaymentType).toBe('FullMonthly');
    expect(submittedValues.interestRate).toBeNull();
  });

  it('Switching Amortising → FullMonthly hides Interest rate but keeps cached value', async () => {
    renderForm({ mode: 'edit', initialValues: initialEditLiability });
    expect(screen.getByLabelText(/interest rate/i)).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText(/repayment type/i));
    fireEvent.click(await screen.findByRole('option', { name: /Full Monthly/i }));
    expect(screen.queryByLabelText(/interest rate/i)).toBeNull();
    fireEvent.click(screen.getByLabelText(/repayment type/i));
    fireEvent.click(await screen.findByRole('option', { name: /Amortising/i }));
    expect(screen.getByLabelText(/interest rate/i)).toHaveValue(0.035);
  });

  it('Advanced disclosure on Edit hides opening balance + start date by default', () => {
    renderForm({ mode: 'edit' });
    expect(screen.queryByLabelText(/opening balance/i)).toBeNull();
    expect(screen.queryByLabelText(/start date/i)).toBeNull();
    expect(screen.getByText(/advanced/i)).toBeInTheDocument();
  });

  it('Expanding the Advanced disclosure on Edit reveals opening balance + start date', () => {
    renderForm({ mode: 'edit' });
    fireEvent.click(screen.getByText(/advanced/i));
    expect(screen.getByLabelText(/opening balance/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/start date/i)).toBeInTheDocument();
  });

  it('Opening balance + start date are inline (no disclosure) on Create', () => {
    renderForm({ mode: 'create' });
    expect(screen.getByLabelText(/opening balance/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/start date/i)).toBeInTheDocument();
    expect(screen.queryByText(/advanced/i)).toBeNull();
  });
});
