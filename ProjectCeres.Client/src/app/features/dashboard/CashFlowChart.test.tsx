import { render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CashFlowChart } from './CashFlowChart';

let mockFetch: ReturnType<typeof vi.fn>;

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
});

describe('CashFlowChart', () => {
  it('renders title and subtitle', () => {
    mockFetch.mockReturnValue(new Promise(() => {}));
    render(<CashFlowChart />);
    expect(screen.getByText('Cash Flow')).toBeInTheDocument();
    expect(screen.getByText('Last 12 months')).toBeInTheDocument();
  });

  it('renders empty state when points is []', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ currencyCode: 'EUR', currencySymbol: '€', points: [] }),
    });
    render(<CashFlowChart />);
    await waitFor(() => {
      expect(screen.getByText('No data yet.')).toBeInTheDocument();
    });
  });

  it('renders CardError on fetch failure', async () => {
    mockFetch.mockRejectedValue(new Error('boom'));
    render(<CashFlowChart />);
    await waitFor(() => {
      expect(screen.getByText(/Couldn't load Cash Flow/)).toBeInTheDocument();
    });
  });

  it('renders chart container when data is present', async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({
        currencyCode: 'EUR',
        currencySymbol: '€',
        points: [
          { month: '2026-03', netFlow: 500 },
          { month: '2026-04', netFlow: -200 },
        ],
      }),
    });
    const { container } = render(<CashFlowChart />);
    await waitFor(() => {
      expect(container.querySelector('.recharts-responsive-container')).toBeInTheDocument();
    });
  });
});
