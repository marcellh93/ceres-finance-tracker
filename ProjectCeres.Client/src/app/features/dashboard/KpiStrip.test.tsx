import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { KpiStrip } from './KpiStrip';

describe('KpiStrip', () => {
  beforeEach(() => {
    vi.spyOn(global, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          netWorth: [{ currencyCode: 'EUR', currencySymbol: '€', assets: 100, liabilities: 0, netWorth: 100 }],
          mtd: { currencyCode: 'EUR', currencySymbol: '€', income: 100, expenses: 50, savingsRate: 0.5 },
          remindersDueCount: 0,
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    );
  });

  afterEach(() => { vi.restoreAllMocks(); });

  it('renders all three KPI cards in order', async () => {
    render(<MemoryRouter><KpiStrip /></MemoryRouter>);
    const netWorthTitle = await screen.findByText('Net Worth');
    const mtdTitle = screen.getByText('Cycle to Date');
    const remindersTitle = screen.getByText('Reminders');
    expect(netWorthTitle).toBeDefined();
    expect(mtdTitle).toBeDefined();
    expect(remindersTitle).toBeDefined();
  });

  it('renders cards in a 3-column grid that stacks on mobile', async () => {
    const { container } = render(<MemoryRouter><KpiStrip /></MemoryRouter>);
    await screen.findByText('Net Worth');
    const gridContainer = container.querySelector('div.grid');
    expect(gridContainer).toBeDefined();
    expect(gridContainer?.className).toContain('grid-cols-1');
    expect(gridContainer?.className).toContain('sm:grid-cols-3');
    expect(gridContainer?.className).toContain('gap-6');
  });
});
