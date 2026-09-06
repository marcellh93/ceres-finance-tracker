import type { ComponentProps } from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { CategoryForm } from './CategoryForm';
import type { CategoryFormValues, CategoryTypeDto } from './categories-api';

const initialCreate: CategoryFormValues = {
  name: '',
  categoryTypeId: 2,        // Expense default
  lifestyleTag: null,
};

const initialEdit: CategoryFormValues = {
  name: 'Groceries',
  categoryTypeId: 2,
  lifestyleTag: 'Needs',
};

const categoryTypes: CategoryTypeDto[] = [
  { id: 1, name: 'Income' },
  { id: 2, name: 'Expense' },
];

function renderForm(overrides?: {
  mode?: 'create' | 'edit';
  initialValues?: CategoryFormValues;
  onSubmit?: ComponentProps<typeof CategoryForm>['onSubmit'];
}) {
  const mode = overrides?.mode ?? 'create';
  const initialValues = overrides?.initialValues ?? (mode === 'create' ? initialCreate : initialEdit);
  const onSubmit = overrides?.onSubmit ?? vi.fn<ComponentProps<typeof CategoryForm>['onSubmit']>().mockResolvedValue({ ok: true });
  const onCancel = vi.fn();
  const utils = render(
    <CategoryForm
      mode={mode}
      initialValues={initialValues}
      categoryTypes={categoryTypes}
      onSubmit={onSubmit}
      onCancel={onCancel}
    />,
  );
  return { ...utils, onSubmit, onCancel };
}

describe('CategoryForm', () => {
  it('renders Name, CategoryType, LifestyleTag with initial values (Edit)', () => {
    renderForm({ mode: 'edit' });
    expect(screen.getByLabelText(/name/i)).toHaveValue('Groceries');
    expect(within(screen.getByLabelText(/type/i)).getByText('Expense')).toBeInTheDocument();
    expect(within(screen.getByLabelText(/lifestyle/i)).getByText('Needs')).toBeInTheDocument();
  });

  it('CategoryType picker is editable on Create', () => {
    renderForm({ mode: 'create' });
    expect(screen.getByLabelText(/type/i)).toHaveAttribute('aria-expanded');
  });

  it('CategoryType is read-only on Edit (no Popover trigger)', () => {
    renderForm({ mode: 'edit' });
    const trigger = screen.getByLabelText(/type/i);
    expect(trigger).not.toHaveAttribute('aria-expanded');
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
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Food' } });
    expect(screen.getByRole('button', { name: /save/i })).toBeEnabled();
  });

  it('Save shows "Saving…" while onSubmit is pending', () => {
    let resolveSubmit: (v: { ok: true }) => void = () => {};
    const onSubmit = vi.fn(
      () => new Promise<{ ok: true }>((resolve) => { resolveSubmit = resolve; }),
    );
    renderForm({ mode: 'edit', onSubmit });
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Food' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    expect(screen.getByRole('button', { name: /saving/i })).toBeDisabled();
    resolveSubmit({ ok: true });
  });

  it('Save greys back out after onSubmit returns ok:true', async () => {
    const onSubmit = vi.fn<ComponentProps<typeof CategoryForm>['onSubmit']>().mockResolvedValue({ ok: true });
    renderForm({ mode: 'edit', onSubmit });
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Food' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /save/i })).toBeDisabled(),
    );
  });

  it('inputs retain edits when onSubmit returns ok:false', async () => {
    const onSubmit = vi.fn().mockResolvedValue({ ok: false });
    renderForm({ mode: 'edit', onSubmit });
    fireEvent.change(screen.getByLabelText(/name/i), { target: { value: 'Food' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /save/i })).toBeEnabled(),
    );
    expect(screen.getByLabelText(/name/i)).toHaveValue('Food');
  });
});
