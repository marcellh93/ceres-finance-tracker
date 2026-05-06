import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { toast } from 'sonner';
import { SaveProfilePrompt } from './SaveProfilePrompt';
import type { ImportColumnMappings } from './import-api';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
}));

const mockFetch = vi.fn();

const mappings: ImportColumnMappings = {
  dateColumn:        'Fecha',
  amountColumn:      'Importe',
  descriptionColumn: 'Concepto',
  categoryColumn:    null,
  flipDebitSign:     true,
  sheetName:         null,
};

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockReset();
});

afterEach(() => vi.resetAllMocks());

describe('SaveProfilePrompt', () => {
  it('renders the inline form with name input and Save/Skip buttons', () => {
    render(<SaveProfilePrompt fileFormat="Csv" mappings={mappings} />);
    expect(screen.getByLabelText(/profile name/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /save/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /skip/i })).toBeInTheDocument();
  });

  it('Save enables once name is non-empty', () => {
    render(<SaveProfilePrompt fileFormat="Csv" mappings={mappings} />);
    fireEvent.change(screen.getByLabelText(/profile name/i), { target: { value: 'Sabadell' } });
    expect(screen.getByRole('button', { name: /save/i })).toBeEnabled();
  });

  it('Save POSTs to /api/import-profiles with the right body and shows confirmation row', async () => {
    mockFetch.mockResolvedValue({ ok: true, status: 201, json: async () => ({ id: 'p-1' }) });
    render(<SaveProfilePrompt fileFormat="Excel" mappings={mappings} />);
    fireEvent.change(screen.getByLabelText(/profile name/i), { target: { value: 'BBVA' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Profile saved.'));

    expect(mockFetch).toHaveBeenCalledWith(
      '/api/import-profiles',
      expect.objectContaining({
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
      }),
    );
    const body = JSON.parse((mockFetch.mock.calls[0][1] as { body: string }).body);
    expect(body).toEqual({
      name:   'BBVA',
      format: 'Excel',
      mappings: { ...mappings, flipDebitSign: true },
    });

    expect(screen.getByText(/saved as/i)).toBeInTheDocument();
    expect(screen.getByText('BBVA')).toBeInTheDocument();
  });

  it('toasts error and stays on the form when the POST fails', async () => {
    mockFetch.mockResolvedValue({ ok: false, status: 500, json: async () => null });
    render(<SaveProfilePrompt fileFormat="Csv" mappings={mappings} />);
    fireEvent.change(screen.getByLabelText(/profile name/i), { target: { value: 'X' } });
    fireEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith("Couldn't save the profile. Try again."),
    );
    expect(screen.queryByText(/saved as/i)).toBeNull();
    expect(screen.getByLabelText(/profile name/i)).toHaveValue('X');
  });

  it('Skip dismisses the prompt entirely', () => {
    render(<SaveProfilePrompt fileFormat="Csv" mappings={mappings} />);
    fireEvent.click(screen.getByRole('button', { name: /skip/i }));
    expect(screen.queryByLabelText(/profile name/i)).toBeNull();
  });
});
