import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { StepMapping } from './StepMapping';
import type {
  HeaderDetectionResult,
  ImportColumnMappings,
  ImportProfileListItemDto,
} from './import-api';

const mockFetch = vi.fn();

const headers: HeaderDetectionResult = {
  headers: ['Fecha', 'Importe', 'Concepto', 'Tipo'],
  dateColumn: 'Fecha',
  amountColumn: 'Importe',
  descriptionColumn: 'Concepto',
  categoryColumn: null,
};

const initialMappings: ImportColumnMappings = {
  dateColumn:        'Fecha',
  amountColumn:      'Importe',
  descriptionColumn: 'Concepto',
  categoryColumn:    null,
  flipDebitSign:     true,
  sheetName:         null,
};

const profile: ImportProfileListItemDto = {
  id: 'p-1',
  name: 'Sabadell Checking',
  format: 'Csv',
  sheetName: null,
  mappings: {
    dateColumn:        'Fecha',
    amountColumn:      'Importe',
    descriptionColumn: 'Concepto',
    categoryColumn:    null,
    flipDebitSign:     true,
    sheetName:         null,
  },
  createdAt: '2026-04-01T00:00:00Z',
  deletedAt: null,
  daysUntilPurge: 0,
};

const mismatchingProfile: ImportProfileListItemDto = {
  ...profile,
  id: 'p-2',
  name: 'BBVA',
  mappings: {
    ...profile.mappings,
    dateColumn: 'OperationDate', // not in headers
  },
};

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation((url: string) => {
    if (url === '/api/import-profiles') {
      return Promise.resolve({ ok: true, status: 200, json: async () => [profile, mismatchingProfile] });
    }
    return Promise.resolve({ ok: false, status: 404, json: async () => null });
  });
});

afterEach(() => vi.resetAllMocks());

describe('StepMapping', () => {
  it('renders the file name and pre-selected columns', async () => {
    render(
      <StepMapping
        headers={headers}
        selectedProfileId={null}
        initialMappings={initialMappings}
        fileName="statement.csv"
        onSelectProfile={() => {}}
        onContinue={() => {}}
        onBack={() => {}}
      />,
    );
    expect(screen.getByText(/statement\.csv/i)).toBeInTheDocument();
    await screen.findByRole('combobox', { name: /saved profile/i });
    expect(screen.getByRole('combobox', { name: /datecolumn/i })).toHaveTextContent('Fecha');
    expect(screen.getByRole('combobox', { name: /amountcolumn/i })).toHaveTextContent('Importe');
  });

  it('Continue is disabled until required columns are non-empty', async () => {
    const empty: ImportColumnMappings = {
      ...initialMappings,
      dateColumn: '',
      amountColumn: '',
      descriptionColumn: '',
    };
    render(
      <StepMapping
        headers={headers}
        selectedProfileId={null}
        initialMappings={empty}
        fileName="statement.csv"
        onSelectProfile={() => {}}
        onContinue={() => {}}
        onBack={() => {}}
      />,
    );
    await screen.findByRole('combobox', { name: /saved profile/i });
    expect(screen.getByRole('button', { name: /continue/i })).toBeDisabled();
  });

  it('Continue forwards the trimmed mappings', async () => {
    const onContinue = vi.fn();
    render(
      <StepMapping
        headers={headers}
        selectedProfileId={null}
        initialMappings={initialMappings}
        fileName="statement.csv"
        onSelectProfile={() => {}}
        onContinue={onContinue}
        onBack={() => {}}
      />,
    );
    await screen.findByRole('combobox', { name: /saved profile/i });
    fireEvent.click(screen.getByRole('button', { name: /continue/i }));
    expect(onContinue).toHaveBeenCalledWith(
      expect.objectContaining({
        dateColumn: 'Fecha',
        amountColumn: 'Importe',
        descriptionColumn: 'Concepto',
        categoryColumn: null,
        flipDebitSign: true,
      }),
    );
  });

  it('warns when a selected profile references columns missing from the file', async () => {
    render(
      <StepMapping
        headers={headers}
        selectedProfileId={mismatchingProfile.id}
        initialMappings={mismatchingProfile.mappings}
        fileName="statement.csv"
        onSelectProfile={() => {}}
        onContinue={() => {}}
        onBack={() => {}}
      />,
    );
    await waitFor(() =>
      expect(screen.getByText(/missing columns the profile expects/i)).toBeInTheDocument(),
    );
    expect(screen.getByText(/'OperationDate'/)).toBeInTheDocument();
  });
});
