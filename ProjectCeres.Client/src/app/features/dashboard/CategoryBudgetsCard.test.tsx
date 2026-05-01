import { render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { CategoryBudgetsCard } from './CategoryBudgetsCard';

describe('CategoryBudgetsCard', () => {
  beforeEach(() => {
    vi.spyOn(global, 'fetch').mockResolvedValue(
      new Response(JSON.stringify([]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );
  });

  afterEach(() => { vi.restoreAllMocks(); });

  it('renders the card header "Category Budgets"', () => {
    render(<MemoryRouter><CategoryBudgetsCard /></MemoryRouter>);
    expect(screen.getByText('Category Budgets')).toBeDefined();
  });

  it('renders a "View all →" link to /budgets?type=category', () => {
    render(<MemoryRouter><CategoryBudgetsCard /></MemoryRouter>);
    const link = screen.getByRole('link', { name: /view all/i });
    expect(link.getAttribute('href')).toBe('/budgets?type=category');
  });
});
