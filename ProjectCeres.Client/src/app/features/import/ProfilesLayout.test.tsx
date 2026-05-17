import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { ProfilesLayout } from './ProfilesLayout';
import type { ImportProfileListItemDto } from './import-api';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

let mockFetch: ReturnType<typeof vi.fn>;

const activeRow: ImportProfileListItemDto = {
  id: 'a-1',
  name: 'Sabadell Checking',
  format: 'Csv',
  sheetName: null,
  mappings: {
    dateColumn: 'Fecha',
    amountColumn: 'Importe',
    descriptionColumn: 'Concepto',
    categoryColumn: null,
    flipDebitSign: true,
    sheetName: null,
  },
  createdAt: '2026-04-01T00:00:00Z',
  deletedAt: null,
  daysUntilPurge: 0,
};

const archivedRow: ImportProfileListItemDto = {
  ...activeRow,
  id: 'a-2',
  name: 'BBVA Old',
  deletedAt: '2026-04-15T00:00:00Z',
  daysUntilPurge: 75,
};

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url === '/api/import-profiles') {
      return Promise.resolve({ ok: true, status: 200, json: async () => [activeRow] });
    }
    if (url === '/api/import-profiles?includeDeleted=true') {
      return Promise.resolve({ ok: true, status: 200, json: async () => [activeRow, archivedRow] });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

afterEach(() => vi.resetAllMocks());

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/import/profiles" element={<ProfilesLayout />}>
          <Route path="new" element={<div data-testid="new-page">NEW</div>} />
          <Route path=":id/edit" element={<div data-testid="edit-page">EDIT</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('ProfilesLayout', () => {
  it('renders skeleton while loading', () => {
    mockFetch.mockImplementation(() => new Promise(() => {}));
    renderAt('/import/profiles');
    expect(screen.getByTestId('profiles-skeleton')).toBeInTheDocument();
  });

  it('renders active profiles by default', async () => {
    renderAt('/import/profiles');
    await screen.findByText('Sabadell Checking');
    expect(screen.queryByText('BBVA Old')).toBeNull();
  });

  it('toggling Include archived shows archived rows with daysUntilPurge', async () => {
    renderAt('/import/profiles');
    await screen.findByText('Sabadell Checking');
    fireEvent.click(screen.getByRole('switch', { name: /include archived/i }));
    await screen.findByText('BBVA Old');
    expect(screen.getByText(/purges in 75d/i)).toBeInTheDocument();
  });

  it('search filters rows', async () => {
    renderAt('/import/profiles');
    await screen.findByText('Sabadell Checking');
    fireEvent.change(screen.getByPlaceholderText(/filter profiles/i), {
      target: { value: 'zzznomatch' },
    });
    await waitFor(() => {
      expect(screen.getByText(/no profiles match/i)).toBeInTheDocument();
    });
  });

  it('renders the empty-state when no profiles exist', async () => {
    mockFetch.mockImplementation(() =>
      Promise.resolve({ ok: true, status: 200, json: async () => [] }),
    );
    renderAt('/import/profiles');
    await waitFor(() =>
      expect(screen.getByText(/no saved profiles yet/i)).toBeInTheDocument(),
    );
  });

  it('renders the child route Outlet without the list when on /new', async () => {
    renderAt('/import/profiles/new');
    expect(await screen.findByTestId('new-page')).toBeInTheDocument();
    expect(screen.queryByText('Sabadell Checking')).toBeNull();
  });
});
