import { describe, it, expect } from 'vitest';
import { formatK, truncateLabel } from './chart-format';

describe('formatK', () => {
  it('returns "0" for zero', () => {
    expect(formatK(0)).toBe('0');
  });

  it('shows half-thousands without rounding to duplicates', () => {
    expect(formatK(500)).toBe('0.5k');
    expect(formatK(1500)).toBe('1.5k');
    expect(formatK(2500)).toBe('2.5k');
  });

  it('strips trailing .0 for whole thousands under 10k', () => {
    expect(formatK(1000)).toBe('1k');
    expect(formatK(2000)).toBe('2k');
    expect(formatK(9000)).toBe('9k');
  });

  it('drops decimals at or above 10k', () => {
    expect(formatK(10000)).toBe('10k');
    expect(formatK(15000)).toBe('15k');
    expect(formatK(123456)).toBe('123k');
  });

  it('handles negative values', () => {
    expect(formatK(-500)).toBe('-0.5k');
    expect(formatK(-1500)).toBe('-1.5k');
  });
});

describe('truncateLabel', () => {
  it('returns the original when under the limit', () => {
    expect(truncateLabel('Short')).toBe('Short');
    expect(truncateLabel('Exactly eighteen!!')).toBe('Exactly eighteen!!');
  });

  it('truncates with ellipsis when over the limit', () => {
    expect(truncateLabel('Pago Asesoría Legal')).toBe('Pago Asesoría Leg…');
    expect(truncateLabel('Pago Consulado Proceso Pasaporte')).toBe('Pago Consulado Pr…');
  });

  it('respects a custom max length', () => {
    expect(truncateLabel('abcdefghij', 5)).toBe('abcd…');
  });
});
