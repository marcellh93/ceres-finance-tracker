import { describe, expect, it } from 'vitest';
import { contrastRatio, formatRatio } from './contrast';

describe('contrast', () => {
  it('parses rgb(r, g, b)', () => {
    expect(contrastRatio('rgb(255, 255, 255)', 'rgb(0, 0, 0)')).toBeCloseTo(21, 1);
  });

  it('drops alpha from rgba(r, g, b, a)', () => {
    expect(contrastRatio('rgba(255, 255, 255, 0.5)', 'rgba(0, 0, 0, 0.5)')).toBeCloseTo(21, 1);
  });

  it('parses oklch(L C h) — primary token', () => {
    // oklch(0.520 0.110 195) is the project's --primary token in light mode.
    // T3.15 computed the corresponding sRGB hex as #007c7c (rgb(0, 124, 124)).
    // Contrast against black is ~4.17:1 (AA for large text).
    const ratio = contrastRatio('oklch(0.520 0.110 195)', 'rgb(0, 0, 0)');
    expect(ratio).toBeGreaterThan(3.5);
    expect(ratio).toBeLessThan(5);
  });

  it('parses oklch(1 0 0) as white', () => {
    expect(contrastRatio('oklch(1 0 0)', 'rgb(0, 0, 0)')).toBeCloseTo(21, 1);
  });

  it('parses oklch(0 0 0) as black', () => {
    expect(contrastRatio('rgb(255, 255, 255)', 'oklch(0 0 0)')).toBeCloseTo(21, 1);
  });

  it('drops alpha from oklch(L C h / α)', () => {
    const withAlpha = contrastRatio('oklch(1 0 0 / 0.5)', 'rgb(0, 0, 0)');
    const withoutAlpha = contrastRatio('oklch(1 0 0)', 'rgb(0, 0, 0)');
    expect(withAlpha).toBeCloseTo(withoutAlpha, 1);
  });

  it('throws on unsupported color spaces (e.g. hsl)', () => {
    expect(() => contrastRatio('hsl(0, 100%, 50%)', 'rgb(0, 0, 0)')).toThrow(/Cannot parse color/);
  });

  it('cross-format contrast (oklch white vs rgb black) returns ~21', () => {
    expect(contrastRatio('oklch(1 0 0)', 'rgb(0, 0, 0)')).toBeCloseTo(21, 1);
  });

  it('formatRatio produces the AAA/AA/Large/Fail label', () => {
    expect(formatRatio(21)).toContain('AAA');
    expect(formatRatio(5.5)).toContain('AA');
    expect(formatRatio(3.5)).toContain('Large only');
    expect(formatRatio(2)).toContain('Fail');
  });
});
