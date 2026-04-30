/** Formats "yyyy-MM" → "MMM yyyy" using the runtime locale. Non-string input returns ''. */
export function formatMonth(value: unknown): string {
  if (typeof value !== 'string') return '';
  const [y, m] = value.split('-').map(Number);
  return new Date(y, m - 1).toLocaleDateString(undefined, { month: 'short', year: 'numeric' });
}
