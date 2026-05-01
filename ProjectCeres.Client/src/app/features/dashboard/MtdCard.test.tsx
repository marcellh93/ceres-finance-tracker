import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MtdCard } from './MtdCard';

function mockSummary(income: number, expenses: number, savingsRate: number) {
  return new Response(
    JSON.stringify({
      netWorth: [],
      mtd: { currencyCode: 'EUR', currencySymbol: '€', income, expenses, savingsRate },
      remindersDueCount: 0,
    }),
    { status: 200, headers: { 'Content-Type': 'application/json' } },
  );
}

describe('MtdCard', () => {
  beforeEach(() => { vi.spyOn(global, 'fetch'); });
  afterEach(() => { vi.restoreAllMocks(); });

  it('renders income with success color, expenses with destructive color', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(mockSummary(3200, 1850.45, 0.4217));
    render(<MtdCard />);
    // useSettings falls back to 'period_decimal' (US format) without explicit mock.
    const income = await screen.findByText(/3,200/);
    expect(income.className).toContain('text-success');
    const expenses = screen.getByText(/1,850/);
    expect(expenses.className).toContain('text-destructive');
  });

  it('formats savings rate as a percentage with one decimal', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(mockSummary(3200, 1850.45, 0.4217));
    render(<MtdCard />);
    expect(await screen.findByText('42.2%')).toBeDefined();
  });

  it('renders empty-state copy when income and expenses are both 0', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(mockSummary(0, 0, 0));
    render(<MtdCard />);
    expect(await screen.findByText(/no transactions this period/i)).toBeDefined();
  });

  it('renders error + retry on fetch failure', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockRejectedValue(new Error('boom'));
    render(<MtdCard />);
    expect(await screen.findByRole('button', { name: 'Retry' })).toBeDefined();
  });
});
