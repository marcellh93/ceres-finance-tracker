import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { ImportWizard } from './ImportWizard';
import type { AccountOptionDto } from '../movements/movements-api';
import { installCsrfFetchMock, resetCsrfCache } from '../../../test/csrf-fetch-mock';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
}));

let mockFetch: ReturnType<typeof vi.fn>;

const accounts: AccountOptionDto[] = [
  { id: 'a-1', name: 'Sabadell Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Checking' },
];

beforeEach(async () => {
  // apiFetch runs a one-time CSRF handshake before the first state-changing
  // request; this mock serves it so queued responses still line up.
  await resetCsrfCache();
  mockFetch = installCsrfFetchMock();
  mockFetch.mockImplementation((url: string, init?: RequestInit) => {
    if (url === '/api/accounts/active') {
      return Promise.resolve({ ok: true, status: 200, json: async () => accounts });
    }
    if (url === '/api/import/headers') {
      return Promise.resolve({
        ok: true,
        status: 200,
        json: async () => ({
          headers: ['Fecha', 'Importe', 'Concepto'],
          dateColumn: 'Fecha',
          amountColumn: 'Importe',
          descriptionColumn: 'Concepto',
          categoryColumn: null,
        }),
      });
    }
    if (url === '/api/import-profiles') {
      // GET returns empty array (no saved profiles).
      if (!init || (init.method ?? 'GET').toUpperCase() === 'GET') {
        return Promise.resolve({ ok: true, status: 200, json: async () => [] });
      }
      // POST (save profile) — Phase 4 doesn't trigger this in the happy-path test.
      return Promise.resolve({ ok: true, status: 201, json: async () => ({ id: 'p-1' }) });
    }
    if (url === '/api/import') {
      return Promise.resolve({
        ok: true,
        status: 200,
        json: async () => ({
          rowsImported:   12,
          rowsReconciled:  8,
          rowsFlagged:     2,
          rowsStaged:      1,
          rowsFailed:      0,
          errors:          [],
        }),
      });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

function makeCsv(name = 'statement.csv', size = 100) {
  const blob = new Blob([new Uint8Array(size)], { type: 'text/csv' });
  return new File([blob], name, { type: 'text/csv' });
}

function renderWizard() {
  return render(
    <MemoryRouter initialEntries={['/import']}>
      <ImportWizard />
    </MemoryRouter>,
  );
}

describe('ImportWizard happy path', () => {
  it('walks step 1 → 2 → 3 → 4 and renders the result tiles', async () => {
    renderWizard();

    // ── Step 1 ────────────────────────────────────────────────
    await screen.findByRole('combobox'); // accounts loaded
    const dropzone = screen.getByRole('button', { name: /upload bank statement file/i });
    fireEvent.drop(dropzone, { dataTransfer: { files: [makeCsv()] } });
    expect(await screen.findByText('statement.csv')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(await screen.findByText('Sabadell Checking'));

    fireEvent.click(screen.getByRole('button', { name: /^continue$/i }));

    // ── Step 2 ────────────────────────────────────────────────
    await screen.findByText(/map columns/i);
    expect(screen.getByText(/statement\.csv/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /^continue$/i }));

    // ── Step 3 ────────────────────────────────────────────────
    await screen.findByText(/review and import/i);
    fireEvent.click(screen.getByRole('button', { name: /^import$/i }));

    // ── Step 4 ────────────────────────────────────────────────
    await waitFor(() => expect(screen.getByText('Imported')).toBeInTheDocument());
    expect(screen.getByText('12')).toBeInTheDocument();
    expect(screen.getByText('Reconciled')).toBeInTheDocument();
    expect(screen.getByText('8')).toBeInTheDocument();

    // Save-as-profile prompt is shown (no profile was selected).
    expect(screen.getByText(/save these settings as a profile/i)).toBeInTheDocument();
  });
});
