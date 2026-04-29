import { describe, expect, it } from 'vitest';
import {
  availableTodayClass,
  burnRateClass,
  incomeDeltaClass,
  runwayClass,
  safeToSpendClass,
} from './dashboardViewHelper';

describe('dashboardViewHelper', () => {
  describe('availableTodayClass', () => {
    it('is success when >= 0', () => {
      expect(availableTodayClass(0)).toBe('text-success');
      expect(availableTodayClass(100)).toBe('text-success');
    });
    it('is destructive when < 0', () => {
      expect(availableTodayClass(-1)).toBe('text-destructive');
    });
  });

  describe('safeToSpendClass', () => {
    it('is foreground when >= 0', () => {
      expect(safeToSpendClass(0)).toBe('text-foreground');
      expect(safeToSpendClass(100)).toBe('text-foreground');
    });
    it('is warning when < 0', () => {
      expect(safeToSpendClass(-1)).toBe('text-warning');
    });
  });

  describe('runwayClass', () => {
    it('is success when >= 6 months', () => {
      expect(runwayClass(6)).toBe('text-success');
      expect(runwayClass(99)).toBe('text-success');
    });
    it('is warning when 3–5.99 months', () => {
      expect(runwayClass(3)).toBe('text-warning');
      expect(runwayClass(5.99)).toBe('text-warning');
    });
    it('is destructive when < 3 months', () => {
      expect(runwayClass(2.99)).toBe('text-destructive');
      expect(runwayClass(0)).toBe('text-destructive');
    });
  });

  describe('incomeDeltaClass', () => {
    it('is success when > 0', () => {
      expect(incomeDeltaClass(0.0001)).toBe('text-success');
    });
    it('is destructive when < 0', () => {
      expect(incomeDeltaClass(-0.0001)).toBe('text-destructive');
    });
    it('is muted when exactly 0', () => {
      expect(incomeDeltaClass(0)).toBe('text-muted-foreground');
    });
  });

  describe('burnRateClass', () => {
    it('is success when < 0.5', () => {
      expect(burnRateClass(0)).toBe('text-success');
      expect(burnRateClass(0.499)).toBe('text-success');
    });
    it('is warning when 0.5–0.8 inclusive', () => {
      expect(burnRateClass(0.5)).toBe('text-warning');
      expect(burnRateClass(0.8)).toBe('text-warning');
    });
    it('is destructive when > 0.8', () => {
      expect(burnRateClass(0.81)).toBe('text-destructive');
      expect(burnRateClass(1.5)).toBe('text-destructive');
    });
  });
});
