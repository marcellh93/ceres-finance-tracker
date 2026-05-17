import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { BudgetEdit } from './BudgetEdit';

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock('../../lib/use-settings', () => ({
  useSettings: () => ({ data: { numberFormat: 'period_decimal', dateFormat: 'DD/MM/YYYY' }, loading: false }),
}));

let mockFetch: ReturnType<typeof vi.fn>;
beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
});

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/budgets/:id/edit" element={<BudgetEdit />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('BudgetEdit', () => {
  it('discriminator=CategoryBudget loads and renders the Category form', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/budgets/abc-123') {
        return Promise.resolve({ ok: true, status: 200, json: async () => ({ id: 'abc-123', kind: 'CategoryBudget' }) });
      }
      if (url === '/api/category-budgets/abc-123') {
        return Promise.resolve({ ok: true, status: 200, json: async () => ({ id: 'abc-123', categoryId: 'c1', currencyId: 1, currencyCode: 'EUR', limitAmount: 600, isActive: true }) });
      }
      if (url === '/api/categories/active') {
        return Promise.resolve({ ok: true, status: 200, json: async () => [{ id: 'c1', name: 'Groceries', categoryTypeName: 'Expense' }] });
      }
      if (url === '/api/currencies') {
        return Promise.resolve({ ok: true, status: 200, json: async () => [{ id: 1, code: 'EUR', symbol: '€' }] });
      }
      if (url.startsWith('/api/category-budgets?')) {
        return Promise.resolve({ ok: true, status: 200, json: async () => [] });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });

    renderAt('/budgets/abc-123/edit');
    await waitFor(() => expect(screen.getByText(/edit category budget/i)).toBeInTheDocument());
  });

  it('discriminator=GoalBudget loads and renders the Goal form (Spending)', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/budgets/abc-456') {
        return Promise.resolve({ ok: true, status: 200, json: async () => ({ id: 'abc-456', kind: 'GoalBudget' }) });
      }
      if (url === '/api/goal-budgets/abc-456') {
        return Promise.resolve({
          ok: true, status: 200,
          json: async () => ({
            id: 'abc-456', name: 'Trip', goalType: 'Spending',
            currencyId: 1, currencyCode: 'EUR',
            targetAmount: 1500,
            startDate: '2026-01-01', endDate: null,
            description: null, isActive: true, linkedAccountId: null,
          }),
        });
      }
      if (url === '/api/accounts/active') {
        return Promise.resolve({ ok: true, status: 200, json: async () => [{ id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' }] });
      }
      if (url === '/api/currencies') {
        return Promise.resolve({ ok: true, status: 200, json: async () => [{ id: 1, code: 'EUR', symbol: '€' }] });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });

    renderAt('/budgets/abc-456/edit');
    await waitFor(() => expect(screen.getByText(/edit spending goal/i)).toBeInTheDocument());
  });

  it('renders Not Found message on 404 discriminator', async () => {
    mockFetch.mockImplementation(() =>
      Promise.resolve({ ok: false, status: 404, json: async () => null }),
    );
    renderAt('/budgets/missing/edit');
    await waitFor(() => expect(screen.getByText(/budget not found/i)).toBeInTheDocument());
  });
});
