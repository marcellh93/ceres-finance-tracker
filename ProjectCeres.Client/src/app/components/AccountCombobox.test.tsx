import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { AccountCombobox } from './AccountCombobox';
import type { AccountOptionDto } from '../features/movements/movements-api';

const accounts: AccountOptionDto[] = [
  { id: 'a1', name: 'Checking', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
  { id: 'a2', name: 'Savings', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Asset' },
  { id: 'a3', name: 'Credit Card', currencyCode: 'EUR', currencySymbol: '€', accountTypeName: 'Liability' },
];

describe('AccountCombobox', () => {
  it('shows the placeholder when no account is selected', () => {
    render(<AccountCombobox accounts={accounts} value={null} onChange={vi.fn()} placeholder="Select account" />);
    expect(screen.getByText('Select account')).toBeInTheDocument();
  });

  it('shows the selected account name when value is set', () => {
    render(<AccountCombobox accounts={accounts} value="a2" onChange={vi.fn()} placeholder="Select account" />);
    expect(screen.getByText('Savings')).toBeInTheDocument();
  });

  it('calls onChange with the account id when an option is picked', () => {
    const onChange = vi.fn();
    render(<AccountCombobox accounts={accounts} value={null} onChange={onChange} placeholder="Select account" />);

    fireEvent.click(screen.getByRole('combobox'));
    fireEvent.click(screen.getByText('Checking'));

    expect(onChange).toHaveBeenCalledWith('a1');
  });

  it('respects the filter prop to narrow options', () => {
    render(
      <AccountCombobox
        accounts={accounts}
        value={null}
        onChange={vi.fn()}
        placeholder="Select"
        filter={(a) => a.accountTypeName !== 'Liability'}
      />,
    );
    fireEvent.click(screen.getByRole('combobox'));
    expect(screen.getByText('Checking')).toBeInTheDocument();
    expect(screen.getByText('Savings')).toBeInTheDocument();
    expect(screen.queryByText('Credit Card')).not.toBeInTheDocument();
  });
});
