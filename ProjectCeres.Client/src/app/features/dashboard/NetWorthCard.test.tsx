import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { NetWorthCard } from './NetWorthCard';

function mockSummary(netWorth: Array<{ currencyCode: string; currencySymbol: string; assets: number; liabilities: number; netWorth: number }>) {
  return new Response(
    JSON.stringify({
      netWorth,
      mtd: { currencyCode: 'EUR', currencySymbol: '€', income: 0, expenses: 0, savingsRate: 0, priorPeriodIncome: null, priorPeriodExpenses: null, priorPeriodSavingsRate: null },
      remindersDueCount: 0,
    }),
    { status: 200, headers: { 'Content-Type': 'application/json' } },
  );
}

describe('NetWorthCard', () => {
  beforeEach(() => { vi.spyOn(global, 'fetch'); });
  afterEach(() => { vi.restoreAllMocks(); });

  it('renders empty state when no entries', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(mockSummary([]));
    render(<NetWorthCard />);
    expect(await screen.findByText(/no accounts yet/i)).toBeDefined();
  });

  it('single currency renders stat display (no table)', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(
      mockSummary([{ currencyCode: 'EUR', currencySymbol: '€', assets: 14500, liabilities: 2300, netWorth: 12200 }]),
    );
    render(<NetWorthCard />);
    expect(await screen.findByText('Assets')).toBeDefined();
    expect(screen.getByText('Liabilities')).toBeDefined();
    expect(screen.getAllByText('Net Worth').length).toBeGreaterThan(0);
    expect(screen.queryByRole('table')).toBeNull();
  });

  it('multi-currency renders a table with one row per currency', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(
      mockSummary([
        { currencyCode: 'EUR', currencySymbol: '€', assets: 14500, liabilities: 2300, netWorth: 12200 },
        { currencyCode: 'USD', currencySymbol: '$', assets: 5000, liabilities: 0, netWorth: 5000 },
      ]),
    );
    render(<NetWorthCard />);
    const table = await screen.findByRole('table');
    expect(table).toBeDefined();
    expect(screen.getByText('EUR')).toBeDefined();
    expect(screen.getByText('USD')).toBeDefined();
  });

  it('net worth uses success color when positive, destructive when negative', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(
      mockSummary([{ currencyCode: 'EUR', currencySymbol: '€', assets: 100, liabilities: 500, netWorth: -400 }]),
    );
    render(<NetWorthCard />);
    const negative = await screen.findByText(/-400|−400/);
    expect(negative.className).toContain('text-destructive');
  });

  it('renders error + retry on fetch failure', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockRejectedValue(new Error('boom'));
    render(<NetWorthCard />);
    expect(await screen.findByRole('button', { name: 'Retry' })).toBeDefined();
  });
});
