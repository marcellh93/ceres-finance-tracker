import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { FinancialHealthCard } from './FinancialHealthCard';
import type { HealthDto } from './api';

const baseHealth: HealthDto = {
  availableToday: 1000,
  safeToSpend: 800,
  imminentBills: 0,
  laterBills: 0,
  budgetReserve: 0,
  runwayMonths: 8,
  avgMonthlyExpense: 1000,
  currentMonthIncome: 3000,
  rollingAverageIncome: 2700,
  incomeDeltaPercent: 0.111,
  budgetBurnRate: 0.45,
  budgetSpentMtd: 143,
  budgetTotalLimit: 350,
  currencyCode: 'EUR',
  currencySymbol: '€',
};

function mockHealth(overrides: Partial<HealthDto> = {}) {
  return new Response(JSON.stringify({ ...baseHealth, ...overrides }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('FinancialHealthCard', () => {
  beforeEach(() => { vi.spyOn(global, 'fetch'); });
  afterEach(() => { vi.restoreAllMocks(); });

  it('renders error + retry on fetch failure', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockRejectedValue(new Error('boom'));
    render(<FinancialHealthCard />);
    expect(await screen.findByRole('button', { name: 'Retry' })).toBeDefined();
  });

  it('renders all four panel labels on success', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(mockHealth());
    render(<FinancialHealthCard />);
    expect(await screen.findByText('Spendable Balance')).toBeDefined();
    expect(screen.getByText('Runway')).toBeDefined();
    expect(screen.getByText('Income vs. Avg')).toBeDefined();
    expect(screen.getByText('Budget Burn Rate')).toBeDefined();
  });

  it('Spendable Balance shows "No asset accounts found" when availableToday is null', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(mockHealth({ availableToday: null }));
    render(<FinancialHealthCard />);
    expect(await screen.findByText(/no asset accounts found/i)).toBeDefined();
  });

  it('Runway shows empty-state copy when runwayMonths is null', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(mockHealth({ runwayMonths: null }));
    render(<FinancialHealthCard />);
    expect(await screen.findByText(/needs 6 months of expense history/i)).toBeDefined();
  });

  it('Income vs. Avg shows empty-state copy when incomeDeltaPercent is null', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(mockHealth({ incomeDeltaPercent: null }));
    render(<FinancialHealthCard />);
    expect(await screen.findByText(/needs 6 months of income history/i)).toBeDefined();
  });

  it('Budget Burn Rate shows empty-state copy when budgetBurnRate is null', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(mockHealth({ budgetBurnRate: null }));
    render(<FinancialHealthCard />);
    expect(await screen.findByText(/no active category budgets/i)).toBeDefined();
  });

  it('hides "Bills due (7 days)" row when imminentBills is 0', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(mockHealth({ imminentBills: 0 }));
    render(<FinancialHealthCard />);
    await screen.findByText('Spendable Balance');
    expect(screen.queryByText(/bills due \(7 days\)/i)).toBeNull();
  });

  it('shows "Bills due (7 days)" row when imminentBills is non-zero', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(mockHealth({ imminentBills: 50 }));
    render(<FinancialHealthCard />);
    expect(await screen.findByText(/bills due \(7 days\)/i)).toBeDefined();
  });

  it('hides "Safe to spend" row when both laterBills and budgetReserve are 0', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(
      mockHealth({ laterBills: 0, budgetReserve: 0, safeToSpend: 800 }),
    );
    render(<FinancialHealthCard />);
    await screen.findByText('Spendable Balance');
    expect(screen.queryByText(/safe to spend/i)).toBeNull();
  });

  it('shows "Safe to spend" row when at least one soft deduction is non-zero', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(
      mockHealth({ laterBills: 100, budgetReserve: 0, safeToSpend: 700 }),
    );
    render(<FinancialHealthCard />);
    expect(await screen.findByText(/safe to spend/i)).toBeDefined();
  });

  it('Burn Rate panel shows spent/total caption when both fields are present', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(
      mockHealth({ budgetBurnRate: 0.41, budgetSpentMtd: 143, budgetTotalLimit: 350 }),
    );
    render(<FinancialHealthCard />);
    expect(await screen.findByText(/143/)).toBeDefined();
    expect(screen.getByText(/350/)).toBeDefined();
    expect(screen.getByText(/spent/i)).toBeDefined();
  });

  it('Runway panel shows avg-monthly-expense caption when present', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(
      mockHealth({ runwayMonths: 8.5, avgMonthlyExpense: 1000 }),
    );
    render(<FinancialHealthCard />);
    expect(await screen.findByText(/\/mo/)).toBeDefined();
  });

  it('Income vs. Avg panel shows current/rolling-avg caption when both fields are present', async () => {
    (global.fetch as ReturnType<typeof vi.spyOn>).mockResolvedValue(
      mockHealth({ incomeDeltaPercent: 0.111, currentMonthIncome: 3000, rollingAverageIncome: 2700 }),
    );
    render(<FinancialHealthCard />);
    expect(await screen.findByText(/\/mo/i)).toBeDefined();
    expect(screen.getAllByText(/2700/).length).toBeGreaterThan(0);
    expect(screen.getByText(/ avg$/)).toBeDefined();
  });
});
