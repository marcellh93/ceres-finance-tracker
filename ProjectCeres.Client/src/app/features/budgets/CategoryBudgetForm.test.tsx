import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CategoryBudgetForm } from './CategoryBudgetForm';

let mockFetch: ReturnType<typeof vi.fn>;

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url === '/api/categories/active') {
      return Promise.resolve({
        ok: true, status: 200,
        json: async () => [
          { id: 'c1', name: 'Groceries', categoryTypeName: 'Expense' },
          { id: 'c2', name: 'Salary',    categoryTypeName: 'Income' },
          { id: 'c3', name: 'Rent',      categoryTypeName: 'Expense' },
        ],
      });
    }
    if (url === '/api/currencies') {
      return Promise.resolve({
        ok: true, status: 200,
        json: async () => [
          { id: 1, code: 'EUR', symbol: '€' },
          { id: 2, code: 'USD', symbol: '$' },
        ],
      });
    }
    if (url.startsWith('/api/category-budgets')) {
      return Promise.resolve({
        ok: true, status: 200,
        json: async () => [
          { id: 'b1', categoryId: 'c1', categoryName: 'Groceries', currencyCode: 'EUR', currencySymbol: '€', limitAmount: 600, isActive: true, currentPeriodSpend: 0, currentPeriodEnd: '2026-05-31' },
        ],
      });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

afterEach(() => vi.resetAllMocks());

vi.mock('../../lib/use-settings', () => ({
  useSettings: () => ({ data: { numberFormat: 'period_decimal', dateFormat: 'DD/MM/YYYY' }, loading: false }),
}));

describe('CategoryBudgetForm', () => {
  it('renders the three required fields', async () => {
    render(
      <CategoryBudgetForm
        mode="create"
        initialValues={{ categoryId: null, currencyId: null, limitAmount: '' }}
        currencySymbol=""
        onSubmit={vi.fn().mockResolvedValue({ ok: true })}
        onCancel={vi.fn()}
      />,
    );
    await waitFor(() =>
      expect(screen.getByText('Category', { selector: 'label' })).toBeInTheDocument(),
    );
    expect(screen.getByText('Currency', { selector: 'label' })).toBeInTheDocument();
    expect(screen.getByText('Limit', { selector: 'label' })).toBeInTheDocument();
  });

  it('submits with the chosen values', async () => {
    const onSubmit = vi.fn().mockResolvedValue({ ok: true });
    render(
      <CategoryBudgetForm
        mode="create"
        initialValues={{ categoryId: 'c1', currencyId: 1, limitAmount: '600' }}
        currencySymbol="€"
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />,
    );
    fireEvent.click(await screen.findByRole('button', { name: /create/i }));
    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith({ categoryId: 'c1', currencyId: 1, limitAmount: '600' });
    });
  });

  it('renders inline errors from a 422 onSubmit result', async () => {
    const onSubmit = vi.fn().mockResolvedValue({
      ok: false,
      errors: { limitAmount: 'Must be positive.' },
    });
    render(
      <CategoryBudgetForm
        mode="create"
        initialValues={{ categoryId: 'c1', currencyId: 1, limitAmount: '0' }}
        currencySymbol="€"
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />,
    );
    fireEvent.click(await screen.findByRole('button', { name: /create/i }));
    await waitFor(() => expect(screen.getByText(/must be positive/i)).toBeInTheDocument());
  });
});
