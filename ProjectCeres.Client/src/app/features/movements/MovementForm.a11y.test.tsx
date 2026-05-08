import { render } from '@testing-library/react';
import { beforeEach, describe, it, vi } from 'vitest';
import { MovementForm, type MovementFormValues } from './MovementForm';
import { expectNoA11yViolations } from '../../lib/test-axe';

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

beforeEach(() => {
  global.fetch = vi.fn(async () => ({
    ok: true,
    status: 200,
    json: async () => [],
  })) as unknown as typeof fetch;
});

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

describe('MovementForm a11y', () => {
  it('Transaction mode has no serious or critical axe violations', async () => {
    const { container } = render(
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
    await expectNoA11yViolations(container);
  });

  it('Transfer mode has no serious or critical axe violations', async () => {
    const { container } = render(
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
    await expectNoA11yViolations(container);
  });

  it('LiabilityPayment mode has no serious or critical axe violations', async () => {
    const { container } = render(
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
    await expectNoA11yViolations(container);
  });
});
