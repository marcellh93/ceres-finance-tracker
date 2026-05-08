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

  it('preserves the input order during search instead of cmdk relevance reorder', () => {
    const sorted: CategoryOptionDto[] = [
      { id: 'c1', name: 'Apparel', categoryTypeName: 'Expense' },
      { id: 'c2', name: 'Auto', categoryTypeName: 'Expense' },
      { id: 'c3', name: 'Bills', categoryTypeName: 'Expense' },
    ];
    render(<CategoryCombobox categories={sorted} value={null} onChange={vi.fn()} placeholder="Select" />);

    fireEvent.click(screen.getByRole('combobox'));

    const searchInput = screen.getByPlaceholderText('Search categories…');
    fireEvent.change(searchInput, { target: { value: 'a' } });

    // Both Apparel and Auto match 'a'; Bills filtered out. Order preserved.
    const items = screen.getAllByRole('option');
    expect(items.map((el) => el.textContent?.replace(/Expense$/, '').trim())).toEqual(['Apparel', 'Auto']);
  });
});
