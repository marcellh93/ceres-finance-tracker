import { describe, expect, it } from 'vitest';
import { formatDate } from './date-format';

describe('formatDate', () => {
  it('renders DD/MM/YYYY', () => {
    expect(formatDate('2026-04-30', 'DD/MM/YYYY')).toBe('30/04/2026');
  });

  it('renders MM/DD/YYYY', () => {
    expect(formatDate('2026-04-30', 'MM/DD/YYYY')).toBe('04/30/2026');
  });

  it('renders YYYY-MM-DD', () => {
    expect(formatDate('2026-04-30', 'YYYY-MM-DD')).toBe('2026-04-30');
  });

  it('falls back to DD/MM/YYYY when format is undefined', () => {
    expect(formatDate('2026-04-30', undefined)).toBe('30/04/2026');
  });

  it('falls back to DD/MM/YYYY for unknown formats', () => {
    expect(formatDate('2026-04-30', 'gibberish')).toBe('30/04/2026');
  });

  it('returns the input unchanged when it is malformed', () => {
    expect(formatDate('not-a-date', 'DD/MM/YYYY')).toBe('not-a-date');
  });
});
