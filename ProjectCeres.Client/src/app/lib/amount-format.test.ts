import { describe, expect, it } from 'vitest';
import {
  amountPlaceholder,
  formatAmountForDisplay,
  formatNumberForDisplay,
  parseAmountToNumber,
  sanitizeAmountInput,
  stripThousandSeparators,
} from './amount-format';

describe('sanitizeAmountInput', () => {
  it('keeps digits and the user’s decimal separator under comma_decimal', () => {
    expect(sanitizeAmountInput('1234,56', 'comma_decimal')).toBe('1234,56');
  });

  it('substitutes a period for the comma when the user is on comma_decimal', () => {
    // The user clearly meant a decimal separator — normalize it to their setting.
    expect(sanitizeAmountInput('1234.56', 'comma_decimal')).toBe('1234,56');
  });

  it('keeps digits and the user’s decimal separator under period_decimal', () => {
    expect(sanitizeAmountInput('1234.56', 'period_decimal')).toBe('1234.56');
  });

  it('substitutes a comma for the period when the user is on period_decimal', () => {
    expect(sanitizeAmountInput('1234,56', 'period_decimal')).toBe('1234.56');
  });

  it('rejects letters and other characters', () => {
    expect(sanitizeAmountInput('1a2b3$', 'period_decimal')).toBe('123');
  });

  it('drops any separator after the first (second separator is noise)', () => {
    // First comma kept; second comma dropped.
    expect(sanitizeAmountInput('12,34,56', 'comma_decimal')).toBe('12,3456');
  });

  it('drops a period after the user already typed a comma decimal', () => {
    // First comma kept; the period would be a second separator → dropped.
    expect(sanitizeAmountInput('12,34.56', 'comma_decimal')).toBe('12,3456');
  });
});

describe('parseAmountToNumber', () => {
  it('parses comma_decimal raw input', () => {
    expect(parseAmountToNumber('1234,56', 'comma_decimal')).toBe(1234.56);
  });

  it('parses period_decimal raw input', () => {
    expect(parseAmountToNumber('1234.56', 'period_decimal')).toBe(1234.56);
  });

  it('returns NaN for empty input', () => {
    expect(parseAmountToNumber('', 'comma_decimal')).toBeNaN();
  });

  it('parses an integer-only input', () => {
    expect(parseAmountToNumber('100', 'comma_decimal')).toBe(100);
  });
});

describe('formatAmountForDisplay', () => {
  it('inserts comma_decimal grouping (period as thousand separator)', () => {
    expect(formatAmountForDisplay('1234,56', 'comma_decimal')).toBe('1.234,56');
    expect(formatAmountForDisplay('1234567,89', 'comma_decimal')).toBe('1.234.567,89');
  });

  it('inserts period_decimal grouping (comma as thousand separator)', () => {
    expect(formatAmountForDisplay('1234.56', 'period_decimal')).toBe('1,234.56');
    expect(formatAmountForDisplay('1234567.89', 'period_decimal')).toBe('1,234,567.89');
  });

  it('handles values without a decimal part', () => {
    expect(formatAmountForDisplay('1234', 'comma_decimal')).toBe('1.234');
    expect(formatAmountForDisplay('1234', 'period_decimal')).toBe('1,234');
  });

  it('preserves an in-progress fractional part with no digits after the separator', () => {
    expect(formatAmountForDisplay('1234,', 'comma_decimal')).toBe('1.234,');
  });

  it('returns empty string for empty input', () => {
    expect(formatAmountForDisplay('', 'comma_decimal')).toBe('');
  });
});

describe('stripThousandSeparators', () => {
  it('strips period thousand separators under comma_decimal', () => {
    expect(stripThousandSeparators('1.234,56', 'comma_decimal')).toBe('1234,56');
  });

  it('strips comma thousand separators under period_decimal', () => {
    expect(stripThousandSeparators('1,234.56', 'period_decimal')).toBe('1234.56');
  });

  it('is a no-op when no thousand separators are present', () => {
    expect(stripThousandSeparators('123', 'comma_decimal')).toBe('123');
  });
});

describe('formatNumberForDisplay', () => {
  it('formats a JS number for comma_decimal display', () => {
    expect(formatNumberForDisplay(1234.56, 'comma_decimal')).toBe('1.234,56');
  });

  it('formats a JS number for period_decimal display', () => {
    expect(formatNumberForDisplay(1234.56, 'period_decimal')).toBe('1,234.56');
  });

  it('rounds to two decimal places', () => {
    expect(formatNumberForDisplay(0.1 + 0.2, 'period_decimal')).toBe('0.30');
  });

  it('returns empty for non-finite values', () => {
    expect(formatNumberForDisplay(NaN, 'period_decimal')).toBe('');
  });
});

describe('amountPlaceholder', () => {
  it('returns 0,00 for comma_decimal', () => {
    expect(amountPlaceholder('comma_decimal')).toBe('0,00');
  });

  it('returns 0.00 for period_decimal', () => {
    expect(amountPlaceholder('period_decimal')).toBe('0.00');
  });
});
