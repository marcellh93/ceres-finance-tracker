import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { BudgetProgressBar } from './BudgetProgressBar';

vi.mock('../../lib/use-settings', () => ({
  useSettings: () => ({ data: { numberFormat: 'period_decimal' }, loading: false }),
}));

describe('BudgetProgressBar', () => {
  it('renders progress and target with currency symbol', () => {
    render(<BudgetProgressBar progress={250} target={1000} currencySymbol="€" />);
    expect(screen.getByText(/€\s*250/)).toBeInTheDocument();
    expect(screen.getByText(/€\s*1,000/)).toBeInTheDocument();
  });

  it('caps the displayed percent at 100 when over target', () => {
    const { container } = render(
      <BudgetProgressBar progress={2000} target={1000} currencySymbol="$" />,
    );
    // base-ui's Progress.Root puts aria-valuenow on the root element
    const root = container.querySelector('[role="progressbar"]')!;
    expect(root.getAttribute('aria-valuenow')).toBe('100');
  });

  it('uses bg-destructive when at or over 100%', () => {
    const { container } = render(
      <BudgetProgressBar progress={1000} target={1000} currencySymbol="€" />,
    );
    expect(container.innerHTML).toContain('bg-destructive');
  });

  it('uses bg-warning between 70% and 99%', () => {
    const { container } = render(
      <BudgetProgressBar progress={750} target={1000} currencySymbol="€" />,
    );
    expect(container.innerHTML).toContain('bg-warning');
  });

  it('uses bg-success below 70%', () => {
    const { container } = render(
      <BudgetProgressBar progress={500} target={1000} currencySymbol="€" />,
    );
    expect(container.innerHTML).toContain('bg-success');
  });
});
