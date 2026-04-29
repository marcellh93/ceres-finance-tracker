import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SpendingByCategoryChart } from './SpendingByCategoryChart';

const mockFetch = vi.fn();

beforeEach(() => { global.fetch = mockFetch as unknown as typeof fetch; });
afterEach(() => { vi.resetAllMocks(); });

describe('SpendingByCategoryChart', () => {
  it('renders title and subtitle', () => {
    mockFetch.mockReturnValue(new Promise(() => {}));
    render(<SpendingByCategoryChart />);
    expect(screen.getByText('Spending by Category')).toBeInTheDocument();
    expect(screen.getByText('This month')).toBeInTheDocument();
  });

  it('renders empty state when slices is []', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', total: 0, slices: [] }),
    });
    render(<SpendingByCategoryChart />);
    await waitFor(() => {
      expect(screen.getByText('No data yet.')).toBeInTheDocument();
    });
  });

  it('renders CardError on fetch failure', async () => {
    mockFetch.mockRejectedValue(new Error('boom'));
    render(<SpendingByCategoryChart />);
    await waitFor(() => {
      expect(screen.getByText(/Couldn't load Spending by Category/)).toBeInTheDocument();
    });
  });

  it('renders chart container when data is present', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        currencyCode: 'EUR',
        currencySymbol: '€',
        total: 500,
        slices: [
          { categoryName: 'Groceries', amount: 300 },
          { categoryName: 'Transport', amount: 200 },
        ],
      }),
    });
    const { container } = render(<SpendingByCategoryChart />);
    await waitFor(() => {
      expect(container.querySelector('.recharts-responsive-container')).toBeInTheDocument();
    });
  });
});
