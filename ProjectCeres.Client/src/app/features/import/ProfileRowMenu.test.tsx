import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { toast } from 'sonner';
import { ProfileRowMenu } from './ProfileRowMenu';
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
});

afterEach(() => vi.resetAllMocks());

function renderMenu(profile: ImportProfileListItemDto, onChanged = vi.fn()) {
  return render(
    <MemoryRouter initialEntries={['/import/profiles']}>
      <Routes>
        <Route
          path="/import/profiles"
          element={<ProfileRowMenu profile={profile} onChanged={onChanged} />}
        />
        <Route
          path="/import/profiles/:id/edit"
          element={<div data-testid="edit-page">EDIT</div>}
        />
      </Routes>
    </MemoryRouter>,
  );
}

describe('ProfileRowMenu', () => {
  it('shows Edit and Archive for active rows', async () => {
    renderMenu(activeRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    expect(await screen.findByText('Edit')).toBeInTheDocument();
    expect(screen.getByText('Archive')).toBeInTheDocument();
    expect(screen.queryByText('Reactivate')).toBeNull();
  });

  it('shows only Reactivate for archived rows', async () => {
    renderMenu(archivedRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    expect(await screen.findByText('Reactivate')).toBeInTheDocument();
    expect(screen.queryByText('Edit')).toBeNull();
    expect(screen.queryByText('Archive')).toBeNull();
  });

  it('clicking Edit navigates to /import/profiles/:id/edit', async () => {
    renderMenu(activeRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Edit'));
    expect(await screen.findByTestId('edit-page')).toBeInTheDocument();
  });

  it('clicking Archive opens the confirm dialog with the profile name', async () => {
    renderMenu(activeRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive'));
    expect(await screen.findByText(/archive 'sabadell checking'\?/i)).toBeInTheDocument();
  });

  it('archive 204 fires toast.success and onChanged', async () => {
    mockFetch.mockResolvedValue({ ok: true, status: 204, json: async () => null });
    const onChanged = vi.fn();
    renderMenu(activeRow, onChanged);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive'));
    fireEvent.click(await screen.findByRole('button', { name: 'Archive' }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Archived.'));
    expect(onChanged).toHaveBeenCalledTimes(1);
  });

  it('archive other-error fires the generic toast', async () => {
    mockFetch.mockResolvedValue({ ok: false, status: 500, json: async () => null });
    renderMenu(activeRow);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Archive'));
    fireEvent.click(await screen.findByRole('button', { name: 'Archive' }));
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Couldn't archive. Try again."),
    );
  });

  it('reactivate 204 fires toast.success and onChanged', async () => {
    mockFetch.mockResolvedValue({ ok: true, status: 204, json: async () => null });
    const onChanged = vi.fn();
    renderMenu(archivedRow, onChanged);
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    fireEvent.click(await screen.findByText('Reactivate'));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Reactivated.'));
    expect(onChanged).toHaveBeenCalledTimes(1);
  });
});
