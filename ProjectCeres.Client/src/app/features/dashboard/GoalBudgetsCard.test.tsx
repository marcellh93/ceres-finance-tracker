import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { GoalBudgetsCard } from './GoalBudgetsCard';

describe('GoalBudgetsCard', () => {
  beforeEach(() => {
    vi.spyOn(global, 'fetch').mockResolvedValue(
      new Response(JSON.stringify([]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
  });

  afterEach(() => { vi.restoreAllMocks(); });

  it('renders the card header "Goal Budgets"', () => {
    render(<MemoryRouter><GoalBudgetsCard /></MemoryRouter>);
    expect(screen.getByText('Goal Budgets')).toBeDefined();
  });

  it('renders a "View all →" link to /budgets?type=goal', () => {
    render(<MemoryRouter><GoalBudgetsCard /></MemoryRouter>);
    const link = screen.getByRole('link', { name: /view all/i });
    expect(link.getAttribute('href')).toBe('/budgets?type=goal');
  });
});
