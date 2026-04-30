import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MovementsFilterBar } from './MovementsFilterBar';

const mockFetch = vi.fn();

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockResolvedValue({
    ok: true,
    json: async () => [
      { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
    ],
  });
});

afterEach(() => { vi.resetAllMocks(); });

function LocationSpy({ onChange }: { onChange: (search: string) => void }) {
  const loc = useLocation();
  onChange(loc.search);
  return null;
}

function renderBar(initial = '/movements') {
  let captured = '';
  render(
    <MemoryRouter initialEntries={[initial]}>
      <Routes>
        <Route
          path="/movements"
          element={
            <>
              <MovementsFilterBar />
              <LocationSpy onChange={(s) => { captured = s; }} />
            </>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
  return () => captured;
}

describe('MovementsFilterBar', () => {
  it('renders the search input and date inputs', () => {
    renderBar();
    expect(screen.getByPlaceholderText(/search description or category/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/from/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/to/i)).toBeInTheDocument();
  });

  it('updates the URL when the from date changes', async () => {
    const getSearch = renderBar();
    fireEvent.change(screen.getByLabelText(/from/i), { target: { value: '2026-04-01' } });
    await waitFor(() => {
      expect(getSearch()).toContain('from=2026-04-01');
    });
  });

  it('clears all params when "Clear" is clicked', async () => {
    const getSearch = renderBar('/movements?q=hi&from=2026-04-01');
    fireEvent.click(screen.getByRole('button', { name: /clear/i }));
    await waitFor(() => {
      expect(getSearch()).toBe('');
    });
  });
});
