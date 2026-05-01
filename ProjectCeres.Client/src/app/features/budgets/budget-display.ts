/**
 * Display helpers for the Budgets surface.
 *
 * Centralizes labels and formatters so both the list pages and the form
 * pages render the same strings.
 */

export const GOAL_TYPE_LABEL: Record<'Spending' | 'Savings', string> = {
  Spending: 'Spending',
  Savings: 'Savings',
};

/**
 * English ordinal suffix.
 * 1 → '1st', 2 → '2nd', 3 → '3rd', 4 → '4th', 11 → '11th', 21 → '21st', etc.
 */
export function ordinal(n: number): string {
  const mod100 = n % 100;
  if (mod100 >= 11 && mod100 <= 13) return `${n}th`;
  switch (n % 10) {
    case 1: return `${n}st`;
    case 2: return `${n}nd`;
    case 3: return `${n}rd`;
    default: return `${n}th`;
  }
}
