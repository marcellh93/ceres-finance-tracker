import type { ComponentProps } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ProfileForm } from './ProfileForm';
import type { ProfileFormValues } from './import-api';

const initialCreate: ProfileFormValues = {
  name:              '',
  format:            'Csv',
  sheetName:         '',
  dateColumn:        '',
  amountColumn:      '',
  descriptionColumn: '',
  categoryColumn:    '',
};

const initialEditCsv: ProfileFormValues = {
  name:              'Sabadell Checking',
  format:            'Csv',
  sheetName:         '',
  dateColumn:        'Fecha',
  amountColumn:      'Importe',
  descriptionColumn: 'Concepto',
  categoryColumn:    '',
};

const initialEditExcel: ProfileFormValues = {
  ...initialEditCsv,
  format:    'Excel',
  sheetName: 'Transactions',
};

function renderForm(overrides?: {
  mode?: 'create' | 'edit';
  initialValues?: ProfileFormValues;
  onSubmit?: ComponentProps<typeof ProfileForm>['onSubmit'];
}) {
  const mode = overrides?.mode ?? 'create';
  const initialValues =
    overrides?.initialValues ?? (mode === 'create' ? initialCreate : initialEditCsv);
  const onSubmit = overrides?.onSubmit ?? vi.fn<ComponentProps<typeof ProfileForm>['onSubmit']>().mockResolvedValue({ ok: true });
  const onCancel = vi.fn();
  const utils = render(
    <ProfileForm
      mode={mode}
      initialValues={initialValues}
      onSubmit={onSubmit}
      onCancel={onCancel}
    />,
  );
  return { ...utils, onSubmit, onCancel };
}

describe('ProfileForm', () => {
  it('renders Name + mapping inputs with initial values (Edit)', () => {
    renderForm({ mode: 'edit' });
    expect(screen.getByLabelText(/^name$/i)).toHaveValue('Sabadell Checking');
    expect(screen.getByLabelText(/date column/i)).toHaveValue('Fecha');
    expect(screen.getByLabelText(/amount column/i)).toHaveValue('Importe');
    expect(screen.getByLabelText(/description column/i)).toHaveValue('Concepto');
  });

  it('Format picker is editable on Create', () => {
    renderForm({ mode: 'create' });
    expect(screen.getByLabelText(/^format$/i)).toHaveAttribute('aria-expanded');
  });

  it('Format is read-only on Edit (no Popover trigger)', () => {
    renderForm({ mode: 'edit' });
    expect(screen.getByLabelText(/^format$/i)).not.toHaveAttribute('aria-expanded');
  });

  it('Sheet name is hidden when format is CSV', () => {
    renderForm({ mode: 'edit', initialValues: initialEditCsv });
    expect(screen.queryByLabelText(/sheet name/i)).not.toBeInTheDocument();
  });

  it('Sheet name is shown when format is Excel', () => {
    renderForm({ mode: 'edit', initialValues: initialEditExcel });
    expect(screen.getByLabelText(/sheet name/i)).toHaveValue('Transactions');
  });

  it('Cancel calls onCancel', () => {
    const { onCancel } = renderForm({ mode: 'edit' });
    fireEvent.click(screen.getByRole('button', { name: /cancel/i }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('Save is disabled when nothing has changed (Edit)', () => {
    renderForm({ mode: 'edit' });
    expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
  });

  it('Save enables when Name changes', () => {
    renderForm({ mode: 'edit' });
    fireEvent.change(screen.getByLabelText(/^name$/i), { target: { value: 'BBVA' } });
    expect(screen.getByRole('button', { name: /save/i })).toBeEnabled();
  });

  it('Save enables when a mapping changes', () => {
    renderForm({ mode: 'edit' });
    fireEvent.change(screen.getByLabelText(/amount column/i), { target: { value: 'Monto' } });
    expect(screen.getByRole('button', { name: /save/i })).toBeEnabled();
  });

  it('greys Save back out after onSubmit returns ok:true', async () => {
    const onSubmit = vi.fn<ComponentProps<typeof ProfileForm>['onSubmit']>().mockResolvedValue({ ok: true });
    renderForm({ mode: 'edit', onSubmit });
    fireEvent.change(screen.getByLabelText(/^name$/i), { target: { value: 'BBVA' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /save/i })).toBeDisabled(),
    );
  });

  it('keeps inputs when onSubmit returns ok:false', async () => {
    const onSubmit = vi.fn().mockResolvedValue({ ok: false });
    renderForm({ mode: 'edit', onSubmit });
    fireEvent.change(screen.getByLabelText(/^name$/i), { target: { value: 'BBVA' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /save/i })).toBeEnabled(),
    );
    expect(screen.getByLabelText(/^name$/i)).toHaveValue('BBVA');
  });
});
