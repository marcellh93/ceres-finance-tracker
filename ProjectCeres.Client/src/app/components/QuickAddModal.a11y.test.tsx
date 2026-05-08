import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { QuickAddModal } from './QuickAddModal';
import { expectNoA11yViolations } from '../lib/test-axe';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

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

beforeEach(() => {
  global.fetch = vi.fn(async (input: RequestInfo | URL) => {
    const url = typeof input === 'string' ? input : input.toString();
    if (url.includes('/api/accounts/active')) {
      return {
        ok: true,
        json: async () => [
          { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
          { id: 'a2', name: 'Savings', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
          { id: 'a3', name: 'Credit Card', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Liability' },
        ],
      } as Response;
    }
    if (url.includes('/api/categories/active')) {
      return {
        ok: true,
        json: async () => [{ id: 'c1', name: 'Groceries', categoryTypeName: 'Expense' }],
      } as Response;
    }
    return { ok: true, status: 201, json: async () => ({ id: 'new-id' }) } as Response;
  }) as unknown as typeof fetch;
});

describe('QuickAddModal a11y', () => {
  it('Transaction tab has no serious or critical axe violations', async () => {
    const { container } = render(<QuickAddModal open={true} onOpenChange={vi.fn()} />);
    await waitFor(() => expect(screen.getByRole('tab', { name: /transaction/i })).toBeInTheDocument());
    await expectNoA11yViolations(container);
  });

  it('Transfer tab has no serious or critical axe violations', async () => {
    const { container } = render(<QuickAddModal open={true} onOpenChange={vi.fn()} />);
    await waitFor(() => expect(screen.getByRole('tab', { name: /transfer/i })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('tab', { name: /transfer/i }));
    await expectNoA11yViolations(container);
  });

  it('Debt Payment tab has no serious or critical axe violations', async () => {
    const { container } = render(<QuickAddModal open={true} onOpenChange={vi.fn()} />);
    await waitFor(() => expect(screen.getByRole('tab', { name: /debt payment/i })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('tab', { name: /debt payment/i }));
    await expectNoA11yViolations(container);
  });
});
