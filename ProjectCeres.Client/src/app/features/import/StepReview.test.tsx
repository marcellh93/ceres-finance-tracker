import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { StepReview } from './StepReview';
import type { ImportColumnMappings } from './import-api';
import type { AccountOptionDto } from '../movements/movements-api';

let mockFetch: ReturnType<typeof vi.fn>;

const accounts: AccountOptionDto[] = [
  { id: 'a-1', name: 'Sabadell Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Checking' },
];

const mappings: ImportColumnMappings = {
  dateColumn:        'Fecha',
  amountColumn:      'Importe',
  descriptionColumn: 'Concepto',
  categoryColumn:    'Tipo',
  flipDebitSign:     true,
  sheetName:         null,
};

beforeEach(() => {
  mockFetch = vi.fn();
  global.fetch = mockFetch as unknown as typeof fetch;
  mockFetch.mockImplementation(() =>
    Promise.resolve({ ok: true, status: 200, json: async () => accounts }),
  );
});

afterEach(() => vi.resetAllMocks());

describe('StepReview', () => {
  it('renders file, account, and mapping rows', async () => {
    render(
      <StepReview
        fileName="statement.csv"
        accountId="a-1"
        mappings={mappings}
        submitting={false}
        onSubmit={() => {}}
        onBack={() => {}}
      />,
    );
    expect(screen.getByText('statement.csv')).toBeInTheDocument();
    await screen.findByText('Sabadell Checking');
    expect(screen.getByText('Fecha')).toBeInTheDocument();
    expect(screen.getByText('Importe')).toBeInTheDocument();
    expect(screen.getByText('Concepto')).toBeInTheDocument();
    expect(screen.getByText('Tipo')).toBeInTheDocument();
  });

  it('Import button calls onSubmit', async () => {
    const onSubmit = vi.fn();
    render(
      <StepReview
        fileName="statement.csv"
        accountId="a-1"
        mappings={mappings}
        submitting={false}
        onSubmit={onSubmit}
        onBack={() => {}}
      />,
    );
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /^import$/i })).toBeEnabled(),
    );
    fireEvent.click(screen.getByRole('button', { name: /^import$/i }));
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });

  it('Back button calls onBack', () => {
    const onBack = vi.fn();
    render(
      <StepReview
        fileName="statement.csv"
        accountId="a-1"
        mappings={mappings}
        submitting={false}
        onSubmit={() => {}}
        onBack={onBack}
      />,
    );
    fireEvent.click(screen.getByRole('button', { name: /back/i }));
    expect(onBack).toHaveBeenCalledTimes(1);
  });

  it('disables both buttons while submitting', () => {
    render(
      <StepReview
        fileName="statement.csv"
        accountId="a-1"
        mappings={mappings}
        submitting={true}
        onSubmit={() => {}}
        onBack={() => {}}
      />,
    );
    expect(screen.getByRole('button', { name: /importing/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /back/i })).toBeDisabled();
  });

  it('omits the Category row when none was mapped', async () => {
    const noCategory: ImportColumnMappings = { ...mappings, categoryColumn: null };
    render(
      <StepReview
        fileName="statement.csv"
        accountId="a-1"
        mappings={noCategory}
        submitting={false}
        onSubmit={() => {}}
        onBack={() => {}}
      />,
    );
    await screen.findByText('Sabadell Checking');
    expect(screen.queryByText(/category column/i)).toBeNull();
  });
});
