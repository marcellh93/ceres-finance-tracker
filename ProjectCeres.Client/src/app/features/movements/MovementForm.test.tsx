import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MovementForm, type MovementFormValues } from './MovementForm';

const emptyValues: MovementFormValues = {
  date: '2026-04-30',
  amount: '',
  description: '',
  accountId: null,
  categoryId: null,
  sourceAccountId: null,
  destAccountId: null,
  assetAccountId: null,
  liabilityAccountId: null,
  isCleared: false,
  budgetId: null,
  needsReview: false,
};

const accounts = [
  { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
  { id: 'a2', name: 'Credit Card', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Liability' },
];
const categories = [
  { id: 'c1', name: 'Salary', categoryTypeName: 'Income' },
  { id: 'c2', name: 'Groceries', categoryTypeName: 'Expense' },
];

const noopSubmit = vi.fn().mockResolvedValue({ ok: true as const });
const noopCancel = vi.fn();

describe('MovementForm', () => {
  it('Test 1: Create-Transaction mode renders expected fields and no Delete button', () => {
    render(
      <MovementForm
        type="Transaction"
        mode="create"
        initialValues={emptyValues}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={noopCancel}
      />,
    );

    // Date input
    expect(screen.getByLabelText(/date/i)).toBeInTheDocument();
    // Amount input
    expect(screen.getByLabelText(/amount/i)).toBeInTheDocument();
    // Account combobox (via placeholder text in the button)
    expect(screen.getByText(/select account/i)).toBeInTheDocument();
    // Category combobox
    expect(screen.getByText(/select category/i)).toBeInTheDocument();
    // Description input
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    // Cleared switch
    expect(screen.getByRole('switch')).toBeInTheDocument();
    // Save button
    expect(screen.getByRole('button', { name: /save/i })).toBeInTheDocument();
    // No Delete button in create mode
    expect(screen.queryByRole('button', { name: /delete/i })).not.toBeInTheDocument();
  });

  it('Test 2: Create-Transfer mode renders source/dest account fields, no Category or Asset/Liability fields', () => {
    render(
      <MovementForm
        type="Transfer"
        mode="create"
        initialValues={emptyValues}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={noopCancel}
      />,
    );

    expect(screen.getByLabelText(/date/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/amount/i)).toBeInTheDocument();
    expect(screen.getByText(/select source/i)).toBeInTheDocument();
    expect(screen.getByText(/select destination/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument();

    // No category
    expect(screen.queryByText(/select category/i)).not.toBeInTheDocument();
    // No asset/liability account fields
    expect(screen.queryByText(/select asset/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/select liability/i)).not.toBeInTheDocument();
  });

  it('Test 3: Create-LiabilityPayment mode renders Asset and Liability account fields', () => {
    render(
      <MovementForm
        type="LiabilityPayment"
        mode="create"
        initialValues={emptyValues}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={noopCancel}
      />,
    );

    expect(screen.getByLabelText(/date/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/amount/i)).toBeInTheDocument();
    expect(screen.getByText(/select asset/i)).toBeInTheDocument();
    expect(screen.getByText(/select liability/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument();

    // No category
    expect(screen.queryByText(/select category/i)).not.toBeInTheDocument();
    // No source/dest account fields
    expect(screen.queryByText(/select source/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/select destination/i)).not.toBeInTheDocument();
  });

  it('Test 4: Edit mode for Transaction shows Delete button; calls onDelete when confirmed', async () => {
    const onDelete = vi.fn();
    render(
      <MovementForm
        type="Transaction"
        mode="edit"
        initialValues={{ ...emptyValues, accountId: 'a1', categoryId: 'c1' }}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={noopCancel}
        onDelete={onDelete}
      />,
    );

    // Delete button should be visible in edit mode
    const deleteBtn = screen.getByRole('button', { name: /delete/i });
    expect(deleteBtn).toBeInTheDocument();

    // Click to open the AlertDialog confirmation
    fireEvent.click(deleteBtn);

    // Wait for the dialog to open — there will now be two "Delete" buttons:
    // the trigger and the AlertDialogAction confirm button.
    // The confirm button has data-slot="alert-dialog-action"; pick the last one.
    const deleteButtons = await screen.findAllByRole('button', { name: /delete/i, hidden: true });
    // Last one is the AlertDialogAction inside the dialog
    fireEvent.click(deleteButtons[deleteButtons.length - 1]);

    expect(onDelete).toHaveBeenCalledOnce();
  });

  it('Test 5: 422 field error — Amount field shows error message from onSubmit', async () => {
    const failSubmit = vi.fn().mockResolvedValue({
      ok: false as const,
      errors: { amount: 'Must be > 0' },
    });

    render(
      <MovementForm
        type="Transaction"
        mode="create"
        initialValues={emptyValues}
        accounts={accounts}
        categories={categories}
        onSubmit={failSubmit}
        onCancel={noopCancel}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    expect(await screen.findByText('Must be > 0')).toBeInTheDocument();
  });

  it('Test 6: 422 _form banner — rendered above the form', async () => {
    const failSubmit = vi.fn().mockResolvedValue({
      ok: false as const,
      errors: { _form: 'Source and destination must differ.' },
    });

    render(
      <MovementForm
        type="Transfer"
        mode="create"
        initialValues={emptyValues}
        accounts={accounts}
        categories={categories}
        onSubmit={failSubmit}
        onCancel={noopCancel}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    expect(await screen.findByText('Source and destination must differ.')).toBeInTheDocument();
  });

  it('Test 7: Cancel button calls onCancel', () => {
    const onCancel = vi.fn();
    render(
      <MovementForm
        type="Transaction"
        mode="create"
        initialValues={emptyValues}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={onCancel}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: /cancel/i }));
    expect(onCancel).toHaveBeenCalledOnce();
  });
});
