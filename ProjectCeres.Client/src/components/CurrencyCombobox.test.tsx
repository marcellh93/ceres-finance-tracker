import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CurrencyCombobox } from './CurrencyCombobox';

let mockFetch: ReturnType<typeof vi.fn>;

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockResolvedValue({
    ok: true,
    status: 200,
    json: async () => [
      { id: 1, code: 'EUR', symbol: '€' },
      { id: 2, code: 'USD', symbol: '$' },
    ],
  });
});

describe('CurrencyCombobox', () => {
  it('renders the placeholder when no value selected', async () => {
    render(<CurrencyCombobox value={null} onChange={vi.fn()} />);
    await waitFor(() => expect(screen.getByText(/select currency/i)).toBeInTheDocument());
  });

  it('lists currencies and fires onChange on select', async () => {
    const onChange = vi.fn();
    render(<CurrencyCombobox value={null} onChange={onChange} />);
    fireEvent.click(screen.getByRole('combobox'));
    await waitFor(() => expect(screen.getByText(/USD/)).toBeInTheDocument());
    fireEvent.click(screen.getByText(/USD/));
    expect(onChange).toHaveBeenCalledWith(2);
  });

  describe('limited to in-use currencies', () => {
    const ALL = [
      { id: 1, code: 'EUR', symbol: '€' },
      { id: 2, code: 'USD', symbol: '$' },
      { id: 3, code: 'GBP', symbol: '£' },
    ];
    const IN_USE = [ALL[0], ALL[1]];

    function mockCurrencyEndpoints() {
      mockFetch.mockImplementation(async (url: string) => ({
        ok: true,
        status: 200,
        json: async () => (String(url).includes('inUse=true') ? IN_USE : ALL),
      }));
    }

    beforeEach(mockCurrencyEndpoints);

    it('offers every currency by default', async () => {
      render(<CurrencyCombobox value={null} onChange={vi.fn()} />);
      fireEvent.click(screen.getByRole('combobox'));
      await waitFor(() => expect(screen.getByText(/GBP/)).toBeInTheDocument());
      expect(mockFetch.mock.calls.map((c) => String(c[0]))).not.toContain('/api/currencies?inUse=true');
    });

    it('offers only the in-use currencies when inUseOnly is set', async () => {
      render(<CurrencyCombobox value={null} onChange={vi.fn()} inUseOnly />);
      fireEvent.click(screen.getByRole('combobox'));
      await waitFor(() => expect(screen.getByText(/USD/)).toBeInTheDocument());
      expect(screen.getByText(/EUR/)).toBeInTheDocument();
      expect(screen.queryByText(/GBP/)).toBeNull();
    });

    it('shows the fallback currency while no value is chosen', async () => {
      render(<CurrencyCombobox value={null} onChange={vi.fn()} inUseOnly fallbackCode="EUR" />);
      await waitFor(() => expect(screen.getByRole('combobox')).toHaveTextContent('€ EUR'));
    });

    it('prefers an explicit value over the fallback', async () => {
      render(<CurrencyCombobox value={2} onChange={vi.fn()} inUseOnly fallbackCode="EUR" />);
      await waitFor(() => expect(screen.getByRole('combobox')).toHaveTextContent('$ USD'));
    });

    it('keeps the placeholder when there is neither a value nor a fallback', async () => {
      render(<CurrencyCombobox value={null} onChange={vi.fn()} inUseOnly placeholder="Currency" />);
      await waitFor(() => expect(mockFetch).toHaveBeenCalled());
      expect(screen.getByRole('combobox')).toHaveTextContent('Currency');
    });

    it('still labels a saved currency that is no longer in the limited list', async () => {
      render(<CurrencyCombobox value={3} onChange={vi.fn()} inUseOnly />);
      await waitFor(() => expect(screen.getByRole('combobox')).toHaveTextContent('£ GBP'));
    });
  });
});
