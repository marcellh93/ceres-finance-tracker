import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { BudgetCreate } from './BudgetCreate';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

vi.mock('../../lib/use-settings', () => ({
  useSettings: () => ({ data: { numberFormat: 'period_decimal', dateFormat: 'DD/MM/YYYY' }, loading: false }),
}));

let mockFetch: ReturnType<typeof vi.fn>;
beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url === '/api/categories/active') {
      return Promise.resolve({ ok: true, status: 200, json: async () => [{ id: 'c1', name: 'Groceries', categoryTypeName: 'Expense' }] });
    }
    if (url === '/api/currencies') {
      return Promise.resolve({ ok: true, status: 200, json: async () => [{ id: 1, code: 'EUR', symbol: '€' }] });
    }
    if (url === '/api/accounts/active') {
      return Promise.resolve({ ok: true, status: 200, json: async () => [{ id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' }] });
    }
    if (url.startsWith('/api/category-budgets?')) {
      return Promise.resolve({ ok: true, status: 200, json: async () => [] });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/budgets/new" element={<BudgetCreate />} />
        <Route path="/budgets" element={<div data-testid="list-page">LIST</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('BudgetCreate', () => {
  it('?type=category renders the Category form heading', async () => {
    renderAt('/budgets/new?type=category');
    await waitFor(() => expect(screen.getByText(/new category budget/i)).toBeInTheDocument());
  });

  it('?type=spending renders the Spending Goal form heading', async () => {
    renderAt('/budgets/new?type=spending');
    await waitFor(() => expect(screen.getByText(/new spending goal/i)).toBeInTheDocument());
  });

  it('?type=savings renders the Savings Goal form heading', async () => {
    renderAt('/budgets/new?type=savings');
    await waitFor(() => expect(screen.getByText(/new savings goal/i)).toBeInTheDocument());
  });

  it('missing ?type bounces to /budgets', async () => {
    renderAt('/budgets/new');
    await waitFor(() => expect(screen.getByTestId('list-page')).toBeInTheDocument());
  });

  it('renders the conflict prompt when server returns 409 with existingIsActive=true', async () => {
    mockFetch.mockImplementation((url: string, init?: RequestInit) => {
      const method = (init?.method ?? 'GET').toUpperCase();
      if (url === '/api/categories/active') {
        return Promise.resolve({ ok: true, status: 200, json: async () => [{ id: 'c1', name: 'Groceries', categoryTypeName: 'Expense' }] });
      }
      if (url === '/api/currencies') {
        return Promise.resolve({ ok: true, status: 200, json: async () => [{ id: 1, code: 'EUR', symbol: '€' }] });
      }
      if (url.startsWith('/api/category-budgets?')) {
        return Promise.resolve({ ok: true, status: 200, json: async () => [] });
      }
      if (method === 'POST' && url === '/api/category-budgets') {
        return Promise.resolve({
          ok: false, status: 409,
          json: async () => ({
            error: {
              code: 'DUPLICATE_BUDGET',
              message: 'A budget for this category and currency already exists.',
              existingBudgetId: 'existing-id',
              existingIsActive: true,
            },
          }),
        });
      }
      return Promise.resolve({ ok: false, status: 404, json: async () => null });
    });

    renderAt('/budgets/new?type=category');
    await screen.findByText(/new category budget/i);

    fireEvent.click(screen.getByRole('button', { name: /create/i }));

    await waitFor(() => {
      expect(screen.getByText(/Edit it instead/i)).toBeInTheDocument();
    });
  });
});
