import { describe, expect, it } from 'vitest';
import { formatMonth } from './format-month';

describe('formatMonth', () => {
  it('formats yyyy-MM as "MMM yyyy"', () => {
    // jsdom defaults to en-US locale
    expect(formatMonth('2026-04')).toBe('Apr 2026');
    expect(formatMonth('2025-12')).toBe('Dec 2025');
    expect(formatMonth('2026-01')).toBe('Jan 2026');
  });
});
