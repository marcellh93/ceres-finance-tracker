import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { MovementsPagination } from './MovementsPagination';

function LocationSpy({ onChange }: { onChange: (s: string) => void }) {
  const loc = useLocation();
  onChange(loc.search);
  return null;
}

function renderPagination(props: { totalCount: number; pageSize: number }, initial = '/movements') {
  let captured = '';
  render(
    <MemoryRouter initialEntries={[initial]}>
      <Routes>
        <Route
          path="/movements"
          element={
            <>
              <MovementsPagination {...props} />
              <LocationSpy onChange={(s) => { captured = s; }} />
            </>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
  return () => captured;
}

describe('MovementsPagination', () => {
  it('renders nothing when totalCount fits in one page', () => {
    renderPagination({ totalCount: 30, pageSize: 50 });
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('renders page links when totalCount exceeds one page', () => {
    renderPagination({ totalCount: 120, pageSize: 50 });
    expect(screen.getByRole('button', { name: '1' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '2' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: '3' })).toBeInTheDocument();
  });

  it('updates ?page= when a page is clicked', async () => {
    const getSearch = renderPagination({ totalCount: 120, pageSize: 50 });
    fireEvent.click(screen.getByRole('button', { name: '2' }));
    await waitFor(() => {
      expect(getSearch()).toContain('page=2');
    });
  });

  it('marks the current page as active', () => {
    renderPagination({ totalCount: 120, pageSize: 50 }, '/movements?page=2');
    const page2 = screen.getByRole('button', { name: '2' });
    expect(page2).toHaveAttribute('aria-current', 'page');
  });
});
