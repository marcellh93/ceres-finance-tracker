import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { CategoryCombobox } from './CategoryCombobox';
import type { CategoryOptionDto } from '../features/movements/movements-api';

const categories: CategoryOptionDto[] = [
  { id: 'c1', name: 'Salary', categoryTypeName: 'Income' },
  { id: 'c2', name: 'Groceries', categoryTypeName: 'Expense' },
];

describe('CategoryCombobox', () => {
  it('shows the placeholder when no category is selected', () => {
    render(<CategoryCombobox categories={categories} value={null} onChange={vi.fn()} placeholder="Select category" />);
    expect(screen.getByText('Select category')).toBeInTheDocument();
  });

  it('shows the selected category name', () => {
    render(<CategoryCombobox categories={categories} value="c1" onChange={vi.fn()} placeholder="Select" />);
    expect(screen.getByText('Salary')).toBeInTheDocument();
  });

  it('calls onChange with the id when an option is picked', () => {
    const onChange = vi.fn();
    render(<CategoryCombobox categories={categories} value={null} onChange={onChange} placeholder="Select" />);

    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(screen.getByText('Groceries'));

    expect(onChange).toHaveBeenCalledWith('c2');
  });
});
