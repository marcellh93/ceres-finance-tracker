/**
 * Map dashboard metric values to design-system text-color classes.
 * One file so the thresholds live in one place; mirrors what the
 * Razor `DashboardViewHelper.cs` does on the server side.
 */

export function availableTodayClass(value: number): string {
  return value >= 0 ? 'text-success' : 'text-destructive';
}

export function safeToSpendClass(value: number): string {
  return value >= 0 ? 'text-foreground' : 'text-warning';
}

export function runwayClass(months: number): string {
  if (months >= 6) return 'text-success';
  if (months >= 3) return 'text-warning';
  return 'text-destructive';
}

export function incomeDeltaClass(deltaFraction: number): string {
  if (deltaFraction > 0) return 'text-success';
  if (deltaFraction < 0) return 'text-destructive';
  return 'text-muted-foreground';
}

export function burnRateClass(rate: number): string {
  if (rate < 0.5) return 'text-success';
  if (rate <= 0.8) return 'text-warning';
  return 'text-destructive';
}
