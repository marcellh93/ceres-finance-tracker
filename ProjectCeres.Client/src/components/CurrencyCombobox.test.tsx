import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CurrencyCombobox } from './CurrencyCombobox';

const mockFetch = vi.fn();

beforeEach(() => {
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

afterEach(() => vi.resetAllMocks());

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
});
