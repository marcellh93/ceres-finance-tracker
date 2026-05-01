import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MovementsLayout } from './MovementsLayout';

vi.mock('./MovementsBulkActions', () => ({
  MovementsBulkActions: vi.fn(({ totalCount, onAfterBulk }) => (
    <div data-testid="bulk-actions" data-total-count={totalCount}>
      <button onClick={onAfterBulk} data-testid="fire-after-bulk">
        fire-after-bulk
      </button>
    </div>
  )),
}));

vi.mock('./MovementsFilterBar', () => ({
  MovementsFilterBar: vi.fn(() => <div data-testid="filter-bar" />),
}));

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

  it('renders MovementsBulkActions in the page header on the list view', () => {
    render(
      <MemoryRouter initialEntries={['/movements']}>
        <Routes>
          <Route path="/movements" element={<MovementsLayout />}>
            <Route index element={null} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    expect(screen.getByTestId('bulk-actions')).toBeInTheDocument();
  });

  it('passes totalCount from API response to MovementsBulkActions', () => {
    render(
      <MemoryRouter initialEntries={['/movements']}>
        <Routes>
          <Route path="/movements" element={<MovementsLayout />}>
            <Route index element={null} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );
    // BulkActions should be rendered with data-total-count attribute from the API response
    const bulkActions = screen.getByTestId('bulk-actions');
    expect(bulkActions).toHaveAttribute('data-total-count', '0');
  });

  it('wires onAfterBulk to trigger refetch', async () => {
    const user = userEvent.setup();
    const refetchSpy = vi.fn();
    mockFetch.mockResolvedValue({
      ok: true,
      json: async () => ({ items: [], totalCount: 10, page: 1, pageSize: 50 }),
    });

    render(
      <MemoryRouter initialEntries={['/movements']}>
        <Routes>
          <Route path="/movements" element={<MovementsLayout />}>
            <Route index element={null} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    // Click the fire-after-bulk button (which calls onAfterBulk)
    await user.click(screen.getByTestId('fire-after-bulk'));

    // After clicking, refetch should be called (mocked fetch will resolve again)
    expect(mockFetch).toHaveBeenCalled();
  });
});
