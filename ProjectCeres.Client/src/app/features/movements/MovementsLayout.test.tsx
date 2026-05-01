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

  it('renders the outlet content for child routes (replaces the list)', () => {
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
  });

  it('renders a "New" dropdown trigger on the list view', () => {
    render(
      <MemoryRouter initialEntries={['/movements']}>
        <Routes>
          <Route path="/movements" element={<MovementsLayout />}>
            <Route index element={null} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(screen.getByRole('button', { name: /new/i })).toBeInTheDocument();
  });

  it('hides the list and "New" trigger while a child route is active', () => {
    render(
      <MemoryRouter initialEntries={['/movements/new']}>
        <Routes>
          <Route path="/movements" element={<MovementsLayout />}>
            <Route path="new" element={<div data-testid="form">FORM</div>} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(screen.getByTestId('form')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /new/i })).toBeNull();
    expect(screen.queryByRole('heading', { name: /movements/i })).toBeNull();
  });
});
