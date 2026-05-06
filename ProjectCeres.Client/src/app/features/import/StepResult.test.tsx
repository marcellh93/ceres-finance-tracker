import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { StepResult } from './StepResult';
import type { ImportColumnMappings, ImportResult } from './import-api';

const mappings: ImportColumnMappings = {
  dateColumn:        'Fecha',
  amountColumn:      'Importe',
  descriptionColumn: 'Concepto',
  categoryColumn:    null,
  flipDebitSign:     true,
  sheetName:         null,
};

function renderResult(overrides: Partial<{
  result: ImportResult;
  selectedProfileId: string | null;
}> = {}) {
  const result: ImportResult = overrides.result ?? {
    rowsImported:   12,
    rowsReconciled:  8,
    rowsFlagged:     2,
    rowsStaged:      1,
    rowsFailed:      0,
    errors:          [],
  };
  return render(
    <MemoryRouter>
      <StepResult
        result={result}
        selectedProfileId={overrides.selectedProfileId ?? null}
        fileFormat="Csv"
        mappings={mappings}
        onImportAnother={() => {}}
      />
    </MemoryRouter>,
  );
}

describe('StepResult', () => {
  it('renders the five summary tiles with correct counts', () => {
    renderResult();
    expect(screen.getByText('Imported')).toBeInTheDocument();
    expect(screen.getByText('12')).toBeInTheDocument();
    expect(screen.getByText('Reconciled')).toBeInTheDocument();
    expect(screen.getByText('8')).toBeInTheDocument();
    expect(screen.getByText('Needs review')).toBeInTheDocument();
    expect(screen.getByText('Staged for transfer')).toBeInTheDocument();
    expect(screen.getByText('Failed')).toBeInTheDocument();
  });

  it('Reconciled tile deep-links to /review?tab=reconciliations when count > 0', () => {
    renderResult();
    const link = screen.getByRole('link', { name: /reconciled/i });
    expect(link).toHaveAttribute('href', '/review?tab=reconciliations');
  });

  it('Staged tile deep-links to /review?tab=transfers when count > 0', () => {
    renderResult();
    const link = screen.getByRole('link', { name: /staged for transfer/i });
    expect(link).toHaveAttribute('href', '/review?tab=transfers');
  });

  it('Reconciled does not link when count is 0', () => {
    renderResult({
      result: {
        rowsImported: 1, rowsReconciled: 0, rowsFlagged: 0, rowsStaged: 0, rowsFailed: 0, errors: [],
      },
    });
    expect(screen.queryByRole('link', { name: /reconciled/i })).toBeNull();
  });

  it('renders the row-errors block when result.errors is non-empty', () => {
    renderResult({
      result: {
        rowsImported: 0, rowsReconciled: 0, rowsFlagged: 0, rowsStaged: 0, rowsFailed: 2,
        errors: ['Row 2026-04-01 -50: bad date', 'Row 2026-04-02 25: bad amount'],
      },
    });
    expect(screen.getByText(/row errors/i)).toBeInTheDocument();
    expect(screen.getByText(/bad date/i)).toBeInTheDocument();
  });

  it('omits Save-as-profile when a profile was selected', () => {
    renderResult({ selectedProfileId: 'p-1' });
    expect(screen.queryByText(/save these settings as a profile/i)).toBeNull();
  });

  it('shows Save-as-profile when no profile was selected', () => {
    renderResult({ selectedProfileId: null });
    expect(screen.getByText(/save these settings as a profile/i)).toBeInTheDocument();
  });
});
