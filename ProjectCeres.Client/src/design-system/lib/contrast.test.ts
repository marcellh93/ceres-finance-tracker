import { describe, expect, it } from 'vitest';
import { contrastRatio, formatRatio } from './contrast';

describe('contrastRatio', () => {
  it('returns 21 for black on white', () => {
    expect(contrastRatio('rgb(0,0,0)', 'rgb(255,255,255)')).toBeCloseTo(21, 0);
  });

  it('returns 1 for identical colors', () => {
    expect(contrastRatio('rgb(120,120,120)', 'rgb(120,120,120)')).toBeCloseTo(1, 1);
  });

  it('is symmetric', () => {
    const a = contrastRatio('rgb(20,40,60)', 'rgb(200,210,220)');
    const b = contrastRatio('rgb(200,210,220)', 'rgb(20,40,60)');
    expect(a).toBeCloseTo(b, 4);
  });
});

describe('formatRatio', () => {
  it('formats with two decimals and AA/AAA labels', () => {
    expect(formatRatio(21)).toBe('21.00 : 1 (AAA)');
    expect(formatRatio(4.6)).toBe('4.60 : 1 (AA)');
    expect(formatRatio(3.2)).toBe('3.20 : 1 (Large only)');
    expect(formatRatio(2.0)).toBe('2.00 : 1 (Fail)');
  });
});
