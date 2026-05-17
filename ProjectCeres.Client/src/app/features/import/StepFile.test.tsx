import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { StepFile } from './StepFile';
import type { AccountOptionDto } from '../movements/movements-api';
import type { HeaderDetectionResult } from './import-api';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
}));

let mockFetch: ReturnType<typeof vi.fn>;

const accounts: AccountOptionDto[] = [
  { id: 'a-1', name: 'Sabadell Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Checking' },
];

const headers: HeaderDetectionResult = {
  headers: ['Fecha', 'Importe', 'Concepto'],
  dateColumn: 'Fecha',
  amountColumn: 'Importe',
  descriptionColumn: 'Concepto',
  categoryColumn: null,
};

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url === '/api/accounts/active') {
      return Promise.resolve({ ok: true, status: 200, json: async () => accounts });
    }
    if (url === '/api/import/headers') {
      return Promise.resolve({ ok: true, status: 200, json: async () => headers });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

function makeCsv(name = 'a.csv', size = 100) {
  const blob = new Blob([new Uint8Array(size)], { type: 'text/csv' });
  return new File([blob], name, { type: 'text/csv' });
}

describe('StepFile', () => {
  it('Continue is disabled until file + account are set', async () => {
    render(
      <StepFile
        file={null}
        accountId={null}
        onFileChange={() => {}}
        onAccountChange={() => {}}
        onContinue={() => {}}
      />,
    );
    await screen.findByRole('combobox', { name: /select an account/i }); // accounts loaded
    expect(screen.getByRole('button', { name: /continue/i })).toBeDisabled();
  });

  it('Continue enables when both file and account are set', async () => {
    render(
      <StepFile
        file={makeCsv()}
        accountId={'a-1'}
        onFileChange={() => {}}
        onAccountChange={() => {}}
        onContinue={() => {}}
      />,
    );
    await screen.findByText('Sabadell Checking');
    expect(screen.getByRole('button', { name: /continue/i })).toBeEnabled();
  });

  it('Continue posts the file and forwards header detection result', async () => {
    const onContinue = vi.fn();
    render(
      <StepFile
        file={makeCsv()}
        accountId={'a-1'}
        onFileChange={() => {}}
        onAccountChange={() => {}}
        onContinue={onContinue}
      />,
    );
    await screen.findByText('Sabadell Checking');
    fireEvent.click(screen.getByRole('button', { name: /continue/i }));
    await waitFor(() => expect(onContinue).toHaveBeenCalledWith(headers));
  });

  it('falls through with empty headers when /api/import/headers fails', async () => {
    mockFetch.mockImplementation((url: string) => {
      if (url === '/api/accounts/active') {
        return Promise.resolve({ ok: true, status: 200, json: async () => accounts });
      }
      return Promise.resolve({ ok: false, status: 500, json: async () => null });
    });
    const onContinue = vi.fn();
    render(
      <StepFile
        file={makeCsv()}
        accountId={'a-1'}
        onFileChange={() => {}}
        onAccountChange={() => {}}
        onContinue={onContinue}
      />,
    );
    await screen.findByText('Sabadell Checking');
    fireEvent.click(screen.getByRole('button', { name: /continue/i }));
    await waitFor(() =>
      expect(onContinue).toHaveBeenCalledWith(
        expect.objectContaining({ headers: [], dateColumn: null }),
      ),
    );
  });
});
