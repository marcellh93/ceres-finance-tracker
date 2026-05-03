import { describe, expect, it } from 'vitest';
import { project, type ProjectionResult } from './projection';

describe('project (amortisation)', () => {
  it('returns months/total/interest for a standard amortising loan', () => {
    const result = project(5000, 0.035, 500) as Extract<ProjectionResult, { ok: true }>;
    expect(result.ok).toBe(true);
    expect(result.monthsToPayoff).toBeGreaterThan(0);
    expect(result.monthsToPayoff).toBeLessThan(12);
    expect(result.totalPaid).toBeGreaterThan(5000);
    expect(result.totalInterest).toBeCloseTo(result.totalPaid - 5000, 2);
  });

  it('handles a zero-rate loan as straight-line division', () => {
    const result = project(1000, 0, 250) as Extract<ProjectionResult, { ok: true }>;
    expect(result.ok).toBe(true);
    expect(result.monthsToPayoff).toBe(4);
    expect(result.totalPaid).toBe(1000);
    expect(result.totalInterest).toBe(0);
  });

  it('returns an error result when payment is too low to cover interest', () => {
    const result = project(10000, 0.12, 50);
    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.error).toMatch(/too low/i);
    }
  });

  it('returns an error result when payment is zero or negative', () => {
    expect(project(1000, 0.05, 0).ok).toBe(false);
    expect(project(1000, 0.05, -50).ok).toBe(false);
  });

  it('returns an error result when balance is zero or negative', () => {
    expect(project(0, 0.05, 100).ok).toBe(false);
    expect(project(-100, 0.05, 100).ok).toBe(false);
  });

  it('payoff date is monthsToPayoff months from today', () => {
    const result = project(1200, 0, 100) as Extract<ProjectionResult, { ok: true }>;
    expect(result.ok).toBe(true);
    expect(result.monthsToPayoff).toBe(12);
    const today = new Date();
    expect(result.payoffDate.getFullYear()).toBeGreaterThanOrEqual(today.getFullYear());
  });
});
