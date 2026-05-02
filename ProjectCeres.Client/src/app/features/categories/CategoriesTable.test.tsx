import { render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { CategoriesTable } from './CategoriesTable';
import type { CategoryListItemDto } from './categories-api';

const SYSTEM_CATEGORY_ID = '20000000-0000-0000-0000-000000000001'; // Opening Balance (isSystem true)
const UNCATEGORIZED_INCOME_ID = '20000000-0000-0000-0000-000000000025';

const rows: CategoryListItemDto[] = [
  { id: 'a-1', name: 'Salary',     categoryTypeId: 1, categoryTypeName: 'Income', lifestyleTag: null,    isActive: true,  isSystem: false },
  { id: 'a-2', name: 'Freelance',  categoryTypeId: 1, categoryTypeName: 'Income', lifestyleTag: null,    isActive: true,  isSystem: false },
  { id: 'a-3', name: 'Old Income', categoryTypeId: 1, categoryTypeName: 'Income', lifestyleTag: null,    isActive: false, isSystem: false },
  { id: SYSTEM_CATEGORY_ID,       name: 'Opening Balance',     categoryTypeId: 1, categoryTypeName: 'Income', lifestyleTag: null, isActive: true, isSystem: true },
  { id: UNCATEGORIZED_INCOME_ID,  name: 'Uncategorized Income', categoryTypeId: 1, categoryTypeName: 'Income', lifestyleTag: null, isActive: true, isSystem: false },
];

function renderTable(props?: Partial<React.ComponentProps<typeof CategoriesTable>>) {
  return render(
    <MemoryRouter>
      <CategoriesTable
        rows={rows}
        onChanged={vi.fn()}
        {...props}
      />
    </MemoryRouter>,
  );
}

describe('CategoriesTable', () => {
  it('renders one row per category', () => {
    renderTable();
    expect(screen.getByText('Salary')).toBeInTheDocument();
    expect(screen.getByText('Freelance')).toBeInTheDocument();
    expect(screen.getByText('Opening Balance')).toBeInTheDocument();
  });

  it('shows the locked-system marker on isSystem rows', () => {
    renderTable();
    const systemRow = screen.getByText('Opening Balance').closest('tr')!;
    expect(within(systemRow).getByRole('button', { name: /system category/i })).toBeInTheDocument();
  });

  it('shows the locked-system marker on RESERVED_UNCATEGORIZED rows even when isSystem=false', () => {
    renderTable();
    const uncategorizedRow = screen.getByText('Uncategorized Income').closest('tr')!;
    expect(within(uncategorizedRow).getByRole('button', { name: /system category/i })).toBeInTheDocument();
  });

  it('shows the Archived badge on rows where isActive=false', () => {
    renderTable();
    const archivedRow = screen.getByText('Old Income').closest('tr')!;
    expect(within(archivedRow).getByText('Archived')).toBeInTheDocument();
  });

  it('renders no row-menu trigger for system or reserved rows', () => {
    renderTable();
    const systemRow = screen.getByText('Opening Balance').closest('tr')!;
    expect(within(systemRow).queryByRole('button', { name: /row actions/i })).toBeNull();
    const uncategorizedRow = screen.getByText('Uncategorized Income').closest('tr')!;
    expect(within(uncategorizedRow).queryByRole('button', { name: /row actions/i })).toBeNull();
  });

  it('renders a row-menu trigger for active user rows', () => {
    renderTable();
    const userRow = screen.getByText('Salary').closest('tr')!;
    expect(within(userRow).getByRole('button', { name: /row actions/i })).toBeInTheDocument();
  });

  it('renders the empty state when rows array is empty', () => {
    renderTable({ rows: [] });
    expect(screen.getByText(/no categories/i)).toBeInTheDocument();
  });
});
