import { describe, it, expect } from 'vitest';
import { getCurrentPeriodMonth, getBoundsForMonth } from './period';

describe('getCurrentPeriodMonth', () => {
  it('startDay=1: always returns calendar month', () => {
    expect(getCurrentPeriodMonth(new Date(2026, 4, 15), 1)).toEqual({ year: 2026, month: 4 });
    expect(getCurrentPeriodMonth(new Date(2026, 0, 1), 1)).toEqual({ year: 2026, month: 0 });
  });

  it('startDay=15: today before the 15th → period ends in current month', () => {
    // May 4: before May 15, so period ends in May
    expect(getCurrentPeriodMonth(new Date(2026, 4, 4), 15)).toEqual({ year: 2026, month: 4 });
  });

  it('startDay=15: today on the 15th → period ends in next month', () => {
    // May 15: on or after May 15, so period ends in June
    expect(getCurrentPeriodMonth(new Date(2026, 4, 15), 15)).toEqual({ year: 2026, month: 5 });
  });

  it('startDay=15: today after the 15th → period ends in next month', () => {
    // May 20: after May 15, period ends in June
    expect(getCurrentPeriodMonth(new Date(2026, 4, 20), 15)).toEqual({ year: 2026, month: 5 });
  });

  it('wraps December correctly: startDay=15, today Dec 20 → Jan next year', () => {
    expect(getCurrentPeriodMonth(new Date(2026, 11, 20), 15)).toEqual({ year: 2027, month: 0 });
  });

  it('startDay=31 in February falls back to last day of month', () => {
    // Feb 28 in a non-leap year: startDay=31 clamps to 28
    const result = getCurrentPeriodMonth(new Date(2026, 1, 28), 31);
    // Feb 28 is exactly equal to the start (31 clamped to 28) → period ends in March
    expect(result).toEqual({ year: 2026, month: 2 });
  });
});

describe('getBoundsForMonth', () => {
  it('startDay=1: returns full calendar month', () => {
    expect(getBoundsForMonth(2026, 4, 1)).toEqual({ from: '2026-05-01', to: '2026-05-31' });
    expect(getBoundsForMonth(2026, 1, 1)).toEqual({ from: '2026-02-01', to: '2026-02-28' });
  });

  it('startDay=15: period spans from 15th of previous month to 14th of current month', () => {
    // period named "May" with startDay=15: Apr 15 – May 14
    expect(getBoundsForMonth(2026, 4, 15)).toEqual({ from: '2026-04-15', to: '2026-05-14' });
  });

  it('startDay=15: period named January wraps from previous year December', () => {
    expect(getBoundsForMonth(2026, 0, 15)).toEqual({ from: '2025-12-15', to: '2026-01-14' });
  });

  it('startDay=31: falls back to last day of short months', () => {
    // Period named March with startDay=31: Feb 28 – Mar 30 (startDay=31 clamps in Feb to 28, in Mar to 31-1=30)
    const result = getBoundsForMonth(2026, 2, 31);
    expect(result.from).toBe('2026-02-28');
    expect(result.to).toBe('2026-03-30');
  });
});
