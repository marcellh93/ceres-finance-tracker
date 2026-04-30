import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MovementsLayout } from './MovementsLayout';

const mockFetch = vi.fn();

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockResolvedValue({
    ok: true,
    json: async () => ({ items: [], totalCount: 0, page: 1, pageSize: 50 }),
  });
});

afterEach(() => { vi.resetAllMocks(); });

describe('MovementsLayout', () => {
  it('renders the Movements heading and the list area', () => {
    render(
      <MemoryRouter initialEntries={['/movements']}>
        <Routes>
          <Route path="/movements" element={<MovementsLayout />}>
            <Route index element={null} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(screen.getByRole('heading', { name: /movements/i })).toBeInTheDocument();
  });

  it('renders the outlet content for child routes', () => {
    render(
      <MemoryRouter initialEntries={['/movements/new']}>
        <Routes>
          <Route path="/movements" element={<MovementsLayout />}>
            <Route path="new" element={<div data-testid="child-route">CHILD</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(screen.getByTestId('child-route')).toHaveTextContent('CHILD');
    // List heading still present (parent stays mounted).
    expect(screen.getByRole('heading', { name: /movements/i })).toBeInTheDocument();
  });

  it('renders a "New" button that links to /movements/new', () => {
    render(
      <MemoryRouter initialEntries={['/movements']}>
        <Routes>
          <Route path="/movements" element={<MovementsLayout />}>
            <Route index element={null} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    const link = screen.getByRole('link', { name: /new/i });
    // The relative `to="new"` resolves to "/movements/new" against the parent route
    expect(link.getAttribute('href')).toBe('/movements/new');
  });
});
