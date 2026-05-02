import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { SettingsForm } from './SettingsForm';
import type { CurrencyOptionDto, SettingsFormValues } from './settings-api';

const initialValues: SettingsFormValues = {
  numberFormat: 'comma_decimal',
  dateFormat: 'DD/MM/YYYY',
  defaultCurrencyId: 1,
  periodStartDay: 1,
};

const currencies: CurrencyOptionDto[] = [
  { id: 1, code: 'EUR', symbol: '€' },
  { id: 2, code: 'USD', symbol: '$' },
];

function renderForm(overrides?: { onSubmit?: ReturnType<typeof vi.fn> }) {
  const onSubmit = overrides?.onSubmit ?? vi.fn().mockResolvedValue({ ok: true });
  const utils = render(
    <SettingsForm
      initialValues={initialValues}
      currencies={currencies}
      onSubmit={onSubmit}
    />,
  );
  return { ...utils, onSubmit };
}

describe('SettingsForm', () => {
  it('renders all four fields with initial values reflected in the trigger labels', () => {
    renderForm();
    // Number Format trigger shows the human label, not the raw enum value.
    const numberFormatTrigger = screen.getByLabelText(/number format/i);
    expect(within(numberFormatTrigger).getByText(/comma decimal/i)).toBeInTheDocument();
    // Date Format trigger.
    const dateFormatTrigger = screen.getByLabelText(/date format/i);
    expect(within(dateFormatTrigger).getByText('DD/MM/YYYY')).toBeInTheDocument();
    // Default Currency trigger shows code (symbol), not the numeric id.
    const currencyTrigger = screen.getByLabelText(/default currency/i);
    expect(within(currencyTrigger).getByText(/EUR/)).toBeInTheDocument();
    // Period Start Day spinbutton shows the initial value.
    expect(screen.getByLabelText(/period start day/i)).toHaveValue(1);
  });

  it('Save and Reset buttons are disabled when nothing has changed', () => {
    renderForm();
    expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /reset/i })).toBeDisabled();
  });

  it('Save and Reset buttons enable when a field changes', () => {
    renderForm();
    fireEvent.change(screen.getByLabelText(/period start day/i), {
      target: { value: '15' },
    });
    expect(screen.getByRole('button', { name: /save/i })).toBeEnabled();
    expect(screen.getByRole('button', { name: /reset/i })).toBeEnabled();
  });

  it('Reset restores values to the last saved snapshot and re-disables both buttons', () => {
    renderForm();
    const startDayInput = screen.getByLabelText(/period start day/i);

    fireEvent.change(startDayInput, { target: { value: '20' } });
    expect(startDayInput).toHaveValue(20);
    expect(screen.getByRole('button', { name: /reset/i })).toBeEnabled();

    fireEvent.click(screen.getByRole('button', { name: /reset/i }));
    expect(startDayInput).toHaveValue(1);
    expect(screen.getByRole('button', { name: /reset/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
  });

  it('Save button shows "Saving…" while onSubmit is pending', () => {
    let resolveSubmit: (v: { ok: true }) => void = () => {};
    const onSubmit = vi.fn(
      () => new Promise<{ ok: true }>((resolve) => { resolveSubmit = resolve; }),
    );
    renderForm({ onSubmit });
    fireEvent.change(screen.getByLabelText(/period start day/i), {
      target: { value: '15' },
    });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    expect(screen.getByRole('button', { name: /saving/i })).toBeDisabled();
    resolveSubmit({ ok: true });
  });

  it('Save button greys back out after onSubmit returns ok:true', async () => {
    const onSubmit = vi.fn().mockResolvedValue({ ok: true });
    renderForm({ onSubmit });
    fireEvent.change(screen.getByLabelText(/period start day/i), {
      target: { value: '15' },
    });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /save/i })).toBeDisabled(),
    );
    expect(screen.getByRole('button', { name: /reset/i })).toBeDisabled();
  });

  it('inputs retain user edits when onSubmit returns ok:false', async () => {
    const onSubmit = vi.fn().mockResolvedValue({ ok: false });
    renderForm({ onSubmit });
    fireEvent.change(screen.getByLabelText(/period start day/i), {
      target: { value: '20' },
    });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /save/i })).toBeEnabled(),
    );
    expect(screen.getByLabelText(/period start day/i)).toHaveValue(20);
    expect(screen.getByRole('button', { name: /reset/i })).toBeEnabled();
  });

  it('Period-start-day clamps values outside 1-31 to the nearest valid value', () => {
    renderForm();
    const input = screen.getByLabelText(/period start day/i);
    fireEvent.change(input, { target: { value: '0' } });
    expect(input).toHaveValue(1);
    fireEvent.change(input, { target: { value: '32' } });
    expect(input).toHaveValue(31);
  });
});
