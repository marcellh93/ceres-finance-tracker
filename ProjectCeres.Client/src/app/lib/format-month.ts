/** Formats "yyyy-MM" → "MMM yyyy" using the runtime locale. */
export function formatMonth(yyyyMm: string): string {
  const [y, m] = yyyyMm.split('-').map(Number);
  return new Date(y, m - 1).toLocaleDateString(undefined, { month: 'short', year: 'numeric' });
}
