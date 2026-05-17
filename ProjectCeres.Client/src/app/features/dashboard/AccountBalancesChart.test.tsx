import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AccountBalancesChart } from './AccountBalancesChart';

let mockFetch: ReturnType<typeof vi.fn>;

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
});
afterEach(() => { vi.resetAllMocks(); });

describe('AccountBalancesChart', () => {
  it('renders title without a subtitle', () => {
    mockFetch.mockReturnValue(new Promise(() => {}));
    render(<AccountBalancesChart />);
    expect(screen.getByText('Account Balances')).toBeInTheDocument();
    expect(screen.queryByText(/Last \d+ months/)).not.toBeInTheDocument();
  });

  it('renders empty state when rows is []', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', rows: [] }),
    });
    render(<AccountBalancesChart />);
    await waitFor(() => {
      expect(screen.getByText('No data yet.')).toBeInTheDocument();
    });
  });

  it('renders CardError on fetch failure', async () => {
    mockFetch.mockRejectedValue(new Error('boom'));
    render(<AccountBalancesChart />);
    await waitFor(() => {
      expect(screen.getByText(/Couldn't load Account Balances/)).toBeInTheDocument();
    });
  });

  it('renders chart container when data is present', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        currencyCode: 'EUR',
        currencySymbol: '€',
        rows: [{ accountName: 'Checking', balance: 5000 }],
      }),
    });
    const { container } = render(<AccountBalancesChart />);
    await waitFor(() => {
      expect(container.querySelector('.recharts-responsive-container')).toBeInTheDocument();
    });
  });
});
