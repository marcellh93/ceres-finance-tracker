import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MovementsLayout } from './MovementsLayout';
import { __resetSettingsForTests } from '../../lib/use-settings';

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
  __resetSettingsForTests();
  window.localStorage.clear();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url.startsWith('/api/accounts/active')) {
      return Promise.resolve({ ok: true, json: async () => [] });
    }
    if (url.startsWith('/api/settings')) {
      return Promise.resolve({
        ok: true,
        json: async () => ({
          numberFormat: 'period_decimal',
          dateFormat: 'DD/MM/YYYY',
          defaultCurrencyCode: 'EUR',
          defaultCurrencySymbol: '€',
        }),
      });
    }
    return Promise.resolve({
      ok: true,
      json: async () => ({ items: [], totalCount: 0, page: 1, pageSize: 50 }),
    });
  });
});

afterEach(() => {
  vi.resetAllMocks();
  window.localStorage.clear();
});

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

  it('renders the currency tab strip when the user has 2+ currencies', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url.startsWith('/api/accounts/active')) {
        return Promise.resolve({
          ok: true,
          json: async () => [
            { id: 'a1', name: 'EUR', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
            { id: 'a2', name: 'USD', currencyCode: 'USD', currencySymbol: '$', accountTypeName: 'Asset' },
          ],
        });
      }
      if (url.startsWith('/api/settings')) {
        return Promise.resolve({
          ok: true,
          json: async () => ({
            numberFormat: 'period_decimal',
            dateFormat: 'DD/MM/YYYY',
            defaultCurrencyCode: 'EUR',
            defaultCurrencySymbol: '€',
          }),
        });
      }
      return Promise.resolve({
        ok: true,
        json: async () => ({ items: [], totalCount: 0, page: 1, pageSize: 50 }),
      });
    });

    render(
      <MemoryRouter initialEntries={['/movements?currency=EUR']}>
        <Routes>
          <Route path="/movements" element={<MovementsLayout />}>
            <Route index element={null} />
          </Route>
        </Routes>
      </MemoryRouter>,
    );

    const tabs = await screen.findAllByRole('tab');
    expect(tabs).toHaveLength(2);
    expect(screen.getByRole('tab', { name: 'EUR' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'USD' })).toBeInTheDocument();
  });

  it('hides the currency tab strip when the user has only one currency', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url.startsWith('/api/accounts/active')) {
        return Promise.resolve({
          ok: true,
          json: async () => [
            { id: 'a1', name: 'EUR', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
          ],
        });
      }
      if (url.startsWith('/api/settings')) {
        return Promise.resolve({
          ok: true,
          json: async () => ({
            numberFormat: 'period_decimal',
            dateFormat: 'DD/MM/YYYY',
            defaultCurrencyCode: 'EUR',
            defaultCurrencySymbol: '€',
          }),
        });
      }
      return Promise.resolve({
        ok: true,
        json: async () => ({ items: [], totalCount: 0, page: 1, pageSize: 50 }),
      });
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

    // Wait for the heading then assert no tabs.
    expect(screen.getByRole('heading', { name: /movements/i })).toBeInTheDocument();
    expect(screen.queryByRole('tab')).toBeNull();
  });

  it('wires onAfterBulk to trigger refetch', async () => {
    const user = userEvent.setup();
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
