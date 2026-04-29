import { describe, expect, it } from 'vitest';
import { chartColors } from './chart-colors';

describe('chartColors', () => {
  it('exposes semantic tokens as CSS var strings', () => {
    expect(chartColors.income).toBe('var(--success)');
    expect(chartColors.expense).toBe('var(--destructive)');
    expect(chartColors.netWorth).toBe('var(--chart-1)');
    expect(chartColors.assets).toBe('var(--chart-2)');
    expect(chartColors.liabilities).toBe('var(--chart-3)');
  });

  it('returns the requested chart palette slot', () => {
    expect(chartColors.slot(1)).toBe('var(--chart-1)');
    expect(chartColors.slot(8)).toBe('var(--chart-8)');
  });
});
