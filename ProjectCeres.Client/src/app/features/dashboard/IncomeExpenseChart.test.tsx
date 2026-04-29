import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { IncomeExpenseChart } from './IncomeExpenseChart';

const mockFetch = vi.fn();

beforeEach(() => { global.fetch = mockFetch as unknown as typeof fetch; });
afterEach(() => { vi.resetAllMocks(); });

describe('IncomeExpenseChart', () => {
  it('renders title and subtitle', () => {
    mockFetch.mockReturnValue(new Promise(() => {}));
    render(<IncomeExpenseChart />);
    expect(screen.getByText('Income vs Expense')).toBeInTheDocument();
    expect(screen.getByText('Last 12 months')).toBeInTheDocument();
  });

  it('renders empty state when points is []', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', points: [] }),
    });
    render(<IncomeExpenseChart />);
    await waitFor(() => {
      expect(screen.getByText('No data yet.')).toBeInTheDocument();
    });
  });

  it('renders CardError on fetch failure', async () => {
    mockFetch.mockRejectedValue(new Error('boom'));
    render(<IncomeExpenseChart />);
    await waitFor(() => {
      expect(screen.getByText(/Couldn't load Income vs Expense/)).toBeInTheDocument();
    });
  });

  it('renders chart container when data is present', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        currencyCode: 'EUR',
        currencySymbol: '€',
        points: [{ month: '2026-04', income: 3000, expenses: 2000 }],
      }),
    });
    const { container } = render(<IncomeExpenseChart />);
    await waitFor(() => {
      expect(container.querySelector('.recharts-responsive-container')).toBeInTheDocument();
    });
  });
});
