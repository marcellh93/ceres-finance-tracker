import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { RecurringTable } from './RecurringTable';
import type { RecurringTransactionListItemDto } from './reminder-status';

const TODAY = '2026-05-03';

function makeRow(overrides: Partial<RecurringTransactionListItemDto> = {}): RecurringTransactionListItemDto {
  return {
    id: '1', name: 'Rent', estimatedAmount: 900,
    accountId: 'a', accountName: 'Checking', currencySymbol: '€',
    categoryId: 'c', categoryName: 'Housing', categoryTypeName: 'Expense',
    frequency: 'Monthly', dayOfPeriod: null, nextDueDate: '2026-04-28',
    isActive: true, reminderBehaviour: 'SnapToCalendarDay',
    ...overrides,
  };
}

const noop = vi.fn();

function renderTable(rows: RecurringTransactionListItemDto[]) {
  return render(
    <MemoryRouter>
      <RecurringTable rows={rows} today={TODAY} onChanged={noop} />
    </MemoryRouter>
  );
}

describe('RecurringTable', () => {
  it('renders a row with visible data columns', () => {
    renderTable([makeRow()]);
    expect(screen.getByText('Rent')).toBeInTheDocument();
    expect(screen.getByText('Checking')).toBeInTheDocument();
    expect(screen.getByText('Housing')).toBeInTheDocument();
    expect(screen.getByText('Monthly')).toBeInTheDocument();
    expect(screen.getByText('€900')).toBeInTheDocument();
  });

  it('renders em-dash when estimatedAmount is null', () => {
    renderTable([makeRow({ estimatedAmount: null })]);
    expect(screen.getByText('—')).toBeInTheDocument();
  });

  it('shows Overdue badge for past nextDueDate', () => {
    renderTable([makeRow({ nextDueDate: '2026-04-01' })]);
    expect(screen.getByText('Overdue')).toBeInTheDocument();
  });

  it('shows Due today badge for today nextDueDate', () => {
    renderTable([makeRow({ nextDueDate: TODAY })]);
    expect(screen.getByText('Due today')).toBeInTheDocument();
  });

  it('shows Archived badge for inactive row', () => {
    renderTable([makeRow({ isActive: false })]);
    expect(screen.getByText('Archived')).toBeInTheDocument();
  });

  it('shows Manual badge for ManualDate behaviour', () => {
    renderTable([makeRow({ reminderBehaviour: 'ManualDate' })]);
    expect(screen.getByText('Manual')).toBeInTheDocument();
  });

  it('applies opacity-60 to archived rows', () => {
    renderTable([makeRow({ isActive: false })]);
    const row = screen.getByRole('row', { name: /Rent/ });
    expect(row.className).toContain('opacity-60');
  });
});
