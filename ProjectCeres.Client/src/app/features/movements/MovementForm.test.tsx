import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MovementForm, type MovementFormValues } from './MovementForm';
import type { GoalBudgetListItemDto } from '../budgets/budgets-api';
import { primeCsrfToken } from '../../../test/csrf-fetch-mock';

// Per-test goal-budget fixture; tests can override before rendering.
let SPENDING_GOALS: GoalBudgetListItemDto[] = [];

let mockFetch: ReturnType<typeof vi.fn>;

beforeEach(async () => {
  await primeCsrfToken();
  SPENDING_GOALS = [];
  mockFetch = vi.fn(async (input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    if (url === '/api/goal-budgets?type=spending&includeArchived=true') {
      return {
        ok: true,
        status: 200,
        json: async () => SPENDING_GOALS,
      } as Response;
    }
    return { ok: true, status: 200, json: async () => [] } as Response;
  });
  global.fetch = mockFetch as unknown as typeof fetch;
});

function makeGoal(overrides: Partial<GoalBudgetListItemDto> = {}): GoalBudgetListItemDto {
  return {
    id: 'g1',
    name: 'Trip',
    goalType: 'Spending',
    currencyCode: 'EUR',
    currencySymbol: '€',
    targetAmount: 1000,
    startDate: '2026-01-01',
    endDate: null,
    description: null,
    isActive: true,
    linkedAccountId: null,
    linkedAccountName: null,
    progress: 0,
    ...overrides,
  };
}

// Stub the settings hook so format-aware fields render synchronously in tests.
// The hook's own behavior is exercised in use-settings.test.ts.
vi.mock('../../lib/use-settings', () => ({
  useSettings: () => ({
    data: {
      numberFormat: 'period_decimal' as const,
      dateFormat: 'MM/DD/YYYY',
      defaultCurrencyCode: 'USD',
      defaultCurrencySymbol: '$',
    },
    loading: false,
  }),
}));

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

  it('Test 8: Create mode heading reads "New <type>"', () => {
    const { rerender } = render(
      <MovementForm
        type="Transaction"
        mode="create"
        initialValues={emptyValues}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={() => {}}
      />,
    );
    expect(
      screen.getByRole('heading', { level: 1, name: /new transaction/i }),
    ).toBeInTheDocument();

    rerender(
      <MovementForm
        type="Transfer"
        mode="create"
        initialValues={emptyValues}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={() => {}}
      />,
    );
    expect(
      screen.getByRole('heading', { level: 1, name: /new transfer/i }),
    ).toBeInTheDocument();

    rerender(
      <MovementForm
        type="LiabilityPayment"
        mode="create"
        initialValues={emptyValues}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={() => {}}
      />,
    );
    expect(
      screen.getByRole('heading', { level: 1, name: /new debt payment/i }),
    ).toBeInTheDocument();
  });

  it('Test 9: Edit mode heading reads "Edit <type>"', () => {
    render(
      <MovementForm
        type="Transaction"
        mode="edit"
        initialValues={emptyValues}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={() => {}}
        onDelete={() => {}}
      />,
    );
    expect(
      screen.getByRole('heading', { level: 1, name: /edit transaction/i }),
    ).toBeInTheDocument();
  });

  it('Test 10: Amount field uses the user format placeholder', () => {
    // The mocked useSettings returns period_decimal at the top of this file.
    render(
      <MovementForm
        type="Transaction"
        mode="create"
        initialValues={emptyValues}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={() => {}}
      />,
    );
    const amount = screen.getByLabelText(/amount/i) as HTMLInputElement;
    expect(amount.placeholder).toBe('0.00');
    expect(amount.getAttribute('inputmode')).toBe('decimal');
    expect(amount.type).toBe('text');
  });

  it('Test 11: Amount field formats with thousands separators on blur', () => {
    render(
      <MovementForm
        type="Transaction"
        mode="create"
        initialValues={emptyValues}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={() => {}}
      />,
    );
    const amount = screen.getByLabelText(/amount/i) as HTMLInputElement;
    fireEvent.focus(amount);
    fireEvent.change(amount, { target: { value: '1234.56' } });
    expect(amount.value).toBe('1234.56');
    fireEvent.blur(amount);
    // period_decimal: thousand separator is comma
    expect(amount.value).toBe('1,234.56');
  });

  it('Test 12: Amount field submits the wire (period-decimal) value', async () => {
    const submit = vi
      .fn()
      .mockResolvedValue({ ok: true as const });
    render(
      <MovementForm
        type="Transaction"
        mode="create"
        initialValues={emptyValues}
        accounts={accounts}
        categories={categories}
        onSubmit={submit}
        onCancel={() => {}}
      />,
    );
    const amount = screen.getByLabelText(/amount/i) as HTMLInputElement;
    fireEvent.focus(amount);
    fireEvent.change(amount, { target: { value: '1234.56' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => {
      expect(submit).toHaveBeenCalledTimes(1);
    });
    const submitted = submit.mock.calls[0][0] as MovementFormValues;
    // Wire format is period decimal regardless of display format.
    expect(submitted.amount).toBe('1234.56');
  });

  it('Test 13: Budget picker NOT rendered when no Spending Goals match the account currency', async () => {
    SPENDING_GOALS = [];
    render(
      <MovementForm
        type="Transaction"
        mode="create"
        initialValues={{ ...emptyValues, accountId: 'a1' }}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={noopCancel}
      />,
    );
    // Wait for the goal-budgets fetch to resolve before asserting absence.
    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith(
        '/api/goal-budgets?type=spending&includeArchived=true',
        expect.anything(),
      );
    });
    expect(screen.queryByLabelText(/budget \(optional\)/i)).not.toBeInTheDocument();
  });

  it('Test 14: Budget picker IS rendered when ≥1 active matching goal exists', async () => {
    SPENDING_GOALS = [makeGoal({ id: 'g1', name: 'Trip', currencyCode: 'EUR', isActive: true })];
    render(
      <MovementForm
        type="Transaction"
        mode="create"
        initialValues={{ ...emptyValues, accountId: 'a1' }}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={noopCancel}
      />,
    );
    // Wait for the budget combobox trigger to appear (aria-label="No budget" when unselected)
    const trigger = await screen.findByRole('combobox', { name: /^No budget$/i });
    expect(trigger).toBeInTheDocument();
    // Open the popover and verify 'Trip' is listed
    fireEvent.click(trigger);
    // Explicit 3000ms timeout: portal-rendered popover under full-suite CPU
    // contention can exceed RTL's default 1000ms findBy (see App.test.tsx 802e937).
    expect(await screen.findByText('Trip', {}, { timeout: 3000 })).toBeInTheDocument();
  });

  it('Test 15: Selecting a goal updates values.budgetId on submit', async () => {
    SPENDING_GOALS = [makeGoal({ id: 'g1', name: 'Trip', currencyCode: 'EUR', isActive: true })];
    const submit = vi.fn().mockResolvedValue({ ok: true as const });
    render(
      <MovementForm
        type="Transaction"
        mode="create"
        initialValues={{ ...emptyValues, accountId: 'a1' }}
        accounts={accounts}
        categories={categories}
        onSubmit={submit}
        onCancel={noopCancel}
      />,
    );
    // Open the budget combobox and pick 'Trip'
    const trigger = await screen.findByRole('combobox', { name: /^No budget$/i });
    fireEvent.click(trigger);
    // Explicit 3000ms timeout: same portal-popover contention path as Test 14.
    fireEvent.click(await screen.findByText('Trip', {}, { timeout: 3000 }));
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() => {
      expect(submit).toHaveBeenCalledTimes(1);
    });
    const submitted = submit.mock.calls[0][0] as MovementFormValues;
    expect(submitted.budgetId).toBe('g1');
  });

  it('Test 16: Edit mode shows the currently-tagged archived goal with "(archived)" suffix', async () => {
    SPENDING_GOALS = [
      makeGoal({ id: 'g-archived', name: 'Old Trip', currencyCode: 'EUR', isActive: false }),
    ];
    render(
      <MovementForm
        type="Transaction"
        mode="edit"
        initialValues={{ ...emptyValues, accountId: 'a1', budgetId: 'g-archived' }}
        accounts={accounts}
        categories={categories}
        onSubmit={noopSubmit}
        onCancel={noopCancel}
      />,
    );
    // The trigger should exist once goals load (aria-label="Old Trip" when archived goal selected)
    const trigger = await screen.findByRole('combobox', { name: /^Old Trip$/i });
    expect(trigger).toBeInTheDocument();
    // Open the popover and verify the archived suffix is shown
    fireEvent.click(trigger);
    expect(await screen.findByText('Old Trip (archived)')).toBeInTheDocument();
  });
});
