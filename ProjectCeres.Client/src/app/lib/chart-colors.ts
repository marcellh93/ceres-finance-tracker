export const chartColors = {
  income: 'var(--success)',
  expense: 'var(--destructive)',
  netWorth: 'var(--chart-1)',
  assets: 'var(--chart-2)',
  liabilities: 'var(--chart-3)',
  slot: (n: 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8): string => `var(--chart-${n})`,
};
