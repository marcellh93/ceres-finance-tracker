import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { QuickAddModal } from './QuickAddModal';

// Mock Sonner so we can assert toast calls
vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

// Stub the settings hook so MoneyInput renders synchronously in tests.
vi.mock('../lib/use-settings', () => ({
  useSettings: () => ({
    data: {
      numberFormat: 'period_decimal' as const,
      dateFormat: 'MM/DD/YYYY',
      defaultCurrencyCode: 'EUR',
      defaultCurrencySymbol: '€',
    },
    loading: false,
  }),
}));

let mockFetch: ReturnType<typeof vi.fn>;

const accountsResponse = {
  ok: true,
  json: async () => [
    { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
    { id: 'a2', name: 'Savings', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
    { id: 'a3', name: 'Credit Card', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Liability' },
  ],
};

const categoriesResponse = {
  ok: true,
  json: async () => [
    { id: 'c1', name: 'Groceries', categoryTypeName: 'Expense' },
  ],
};

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url.includes('/api/accounts/active')) return Promise.resolve(accountsResponse);
    if (url.includes('/api/categories/active')) return Promise.resolve(categoriesResponse);
    return Promise.resolve({ ok: true, status: 201, json: async () => ({ id: 'new-id' }) });
  });
});

afterEach(() => {
  vi.resetAllMocks();
});

describe('QuickAddModal', () => {
  it('renders three tabs when open', async () => {
    render(<QuickAddModal open={true} onOpenChange={vi.fn()} />);
    await waitFor(() => {
      expect(screen.getByRole('tab', { name: /transaction/i })).toBeInTheDocument();
      expect(screen.getByRole('tab', { name: /transfer/i })).toBeInTheDocument();
      expect(screen.getByRole('tab', { name: /debt payment/i })).toBeInTheDocument();
    });
  });

  it('Transaction tab POSTs to /api/transactions on submit', async () => {
    const onOpenChange = vi.fn();
    const onSaved = vi.fn();
    render(<QuickAddModal open={true} onOpenChange={onOpenChange} onSaved={onSaved} />);

    await waitFor(() => screen.getByRole('tab', { name: /transaction/i }));

    fireEvent.change(screen.getByLabelText(/amount/i), { target: { value: '25.50' } });
    // Pick first account
    fireEvent.click(screen.getAllByRole('combobox')[0]);
    fireEvent.click(await screen.findByText('Checking'));
    // Pick first category
    fireEvent.click(screen.getAllByRole('combobox')[1]);
    fireEvent.click(await screen.findByText('Groceries'));

    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => {
      const txCall = mockFetch.mock.calls.find((call) => call[0] === '/api/transactions');
      expect(txCall).toBeDefined();
    });

    await waitFor(() => {
      expect(onOpenChange).toHaveBeenCalledWith(false);
      expect(onSaved).toHaveBeenCalled();
    });
  });

  it('Transfer tab shows source and destination account fields, no category', async () => {
    render(<QuickAddModal open={true} onOpenChange={vi.fn()} />);
    fireEvent.click(await screen.findByRole('tab', { name: /transfer/i }));

    expect(screen.getByText(/source account/i)).toBeInTheDocument();
    expect(screen.getByText(/destination account/i)).toBeInTheDocument();
    expect(screen.queryByText(/^category$/i)).not.toBeInTheDocument();
  });

  it('Debt Payment tab filters destination to liability accounts', async () => {
    render(<QuickAddModal open={true} onOpenChange={vi.fn()} />);
    fireEvent.click(await screen.findByRole('tab', { name: /debt payment/i }));

    // Destination combobox should only show liability accounts (Credit Card)
    fireEvent.click(screen.getAllByRole('combobox')[1]);
    expect(await screen.findByText('Credit Card')).toBeInTheDocument();
    expect(screen.queryByText('Checking')).not.toBeInTheDocument();
  });

  it('renders inline errors on 422 ValidationProblem', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url.includes('/api/accounts/active')) return Promise.resolve(accountsResponse);
      if (url.includes('/api/categories/active')) return Promise.resolve(categoriesResponse);
      return Promise.resolve({
        ok: false,
        status: 422,
        json: async () => ({ errors: { Amount: ['Amount must be greater than zero.'] } }),
      });
    });

    render(<QuickAddModal open={true} onOpenChange={vi.fn()} />);

    await waitFor(() => screen.getByRole('tab', { name: /transaction/i }));
    fireEvent.change(screen.getByLabelText(/amount/i), { target: { value: '0' } });
    fireEvent.click(screen.getAllByRole('combobox')[0]);
    fireEvent.click(await screen.findByText('Checking'));
    fireEvent.click(screen.getAllByRole('combobox')[1]);
    fireEvent.click(await screen.findByText('Groceries'));

    fireEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => {
      expect(screen.getByText('Amount must be greater than zero.')).toBeInTheDocument();
    });
  });
});
