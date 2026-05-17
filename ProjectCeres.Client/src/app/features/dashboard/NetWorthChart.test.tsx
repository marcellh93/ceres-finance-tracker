import { render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { NetWorthChart } from './NetWorthChart';

let mockFetch: ReturnType<typeof vi.fn>;

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
});

afterEach(() => {
  vi.resetAllMocks();
});

describe('NetWorthChart', () => {
  it('renders title and subtitle', () => {
    mockFetch.mockReturnValue(new Promise(() => {})); // never resolves
    render(<NetWorthChart />);
    expect(screen.getByText('Net Worth Over Time')).toBeInTheDocument();
    expect(screen.getByText('Last 12 months')).toBeInTheDocument();
  });

  it('renders empty state when points is []', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', points: [] }),
    });
    render(<NetWorthChart />);
    await waitFor(() => {
      expect(screen.getByText('No data yet.')).toBeInTheDocument();
    });
  });

  it('renders CardError on fetch failure', async () => {
    mockFetch.mockRejectedValue(new Error('boom'));
    render(<NetWorthChart />);
    await waitFor(() => {
      expect(screen.getByText(/Couldn't load Net Worth Over Time/)).toBeInTheDocument();
    });
  });

  it('renders chart container when data is present', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        currencyCode: 'EUR',
        currencySymbol: '€',
        points: [
          { month: '2026-03', assets: 1000, liabilities: 200, netWorth: 800 },
          { month: '2026-04', assets: 1100, liabilities: 200, netWorth: 900 },
        ],
      }),
    });
    const { container } = render(<NetWorthChart />);
    await waitFor(() => {
      expect(container.querySelector('.recharts-responsive-container')).toBeInTheDocument();
    });
  });
});
