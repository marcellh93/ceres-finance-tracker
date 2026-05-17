import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { GoalBudgetForm, type GoalBudgetFormValues } from './GoalBudgetForm';

let mockFetch: ReturnType<typeof vi.fn>;
beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url === '/api/accounts/active') {
      return Promise.resolve({
        ok: true, status: 200,
        json: async () => [
          { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
          { id: 'a2', name: 'Credit Card', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Liability' },
        ],
      });
    }
    if (url === '/api/currencies') {
      return Promise.resolve({
        ok: true, status: 200,
        json: async () => [{ id: 1, code: 'EUR', symbol: '€' }, { id: 2, code: 'USD', symbol: '$' }],
      });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

vi.mock('../../lib/use-settings', () => ({
  useSettings: () => ({ data: { numberFormat: 'period_decimal' }, loading: false }),
}));

const baseValues: GoalBudgetFormValues = {
  name: '', goalType: 'Spending', currencyId: null, targetAmount: '',
  startDate: '2026-01-01', endDate: null, description: null, linkedAccountId: null,
};

describe('GoalBudgetForm', () => {
  it('Spending: renders Currency picker, no Linked account', () => {
    render(
      <GoalBudgetForm
        mode="create" goalType="Spending"
        initialValues={baseValues}
        onSubmit={vi.fn().mockResolvedValue({ ok: true })}
        onCancel={vi.fn()}
      />,
    );
    expect(screen.getByText('Currency')).toBeInTheDocument();
    expect(screen.queryByText('Linked account')).not.toBeInTheDocument();
  });

  it('Savings: renders Linked account picker, no Currency picker', () => {
    render(
      <GoalBudgetForm
        mode="create" goalType="Savings"
        initialValues={{ ...baseValues, goalType: 'Savings' }}
        onSubmit={vi.fn().mockResolvedValue({ ok: true })}
        onCancel={vi.fn()}
      />,
    );
    expect(screen.getByText('Linked account')).toBeInTheDocument();
    expect(screen.queryByText(/^Currency$/)).not.toBeInTheDocument();
  });

  it('Submits with form values', async () => {
    const onSubmit = vi.fn().mockResolvedValue({ ok: true });
    render(
      <GoalBudgetForm
        mode="create" goalType="Spending"
        initialValues={{ ...baseValues, name: 'Trip', currencyId: 1, targetAmount: '1500' }}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />,
    );
    fireEvent.click(await screen.findByRole('button', { name: /create/i }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalled());
    const submitted = onSubmit.mock.calls[0][0];
    expect(submitted.name).toBe('Trip');
    expect(submitted.currencyId).toBe(1);
  });

  it('Blocks submit when EndDate < StartDate', async () => {
    const onSubmit = vi.fn();
    render(
      <GoalBudgetForm
        mode="create" goalType="Spending"
        initialValues={{ ...baseValues, name: 'X', currencyId: 1, targetAmount: '100', startDate: '2026-06-01', endDate: '2026-05-01' }}
        onSubmit={onSubmit}
        onCancel={vi.fn()}
      />,
    );
    fireEvent.click(await screen.findByRole('button', { name: /create/i }));
    await waitFor(() => {
      expect(screen.getByText(/end date must be on or after start date/i)).toBeInTheDocument();
    });
    expect(onSubmit).not.toHaveBeenCalled();
  });
});
