import { describe, expect, it } from 'vitest';
import { ordinal } from './budget-display';

describe('ordinal', () => {
  it.each([
    [1, '1st'], [2, '2nd'], [3, '3rd'], [4, '4th'], [10, '10th'],
    [11, '11th'], [12, '12th'], [13, '13th'],
    [21, '21st'], [22, '22nd'], [23, '23rd'], [24, '24th'],
    [31, '31st'],
  ])('formats %i as %s', (input, expected) => {
    expect(ordinal(input)).toBe(expected);
  });
});
