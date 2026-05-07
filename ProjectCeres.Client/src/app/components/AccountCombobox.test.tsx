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

  it('renders a clear button when onClear is set and a value is selected', () => {
    const onClear = vi.fn();
    render(
      <AccountCombobox
        accounts={accounts}
        value="a2"
        onChange={vi.fn()}
        onClear={onClear}
        placeholder="Select"
      />,
    );
    expect(screen.getByRole('button', { name: /clear selection/i })).toBeInTheDocument();
  });

  it('does not render a clear button when no value is selected', () => {
    render(
      <AccountCombobox
        accounts={accounts}
        value={null}
        onChange={vi.fn()}
        onClear={vi.fn()}
        placeholder="Select"
      />,
    );
    expect(screen.queryByRole('button', { name: /clear selection/i })).toBeNull();
  });

  it('does not render a clear button when onClear is omitted, even with a value selected', () => {
    render(
      <AccountCombobox
        accounts={accounts}
        value="a2"
        onChange={vi.fn()}
        placeholder="Select"
      />,
    );
    expect(screen.queryByRole('button', { name: /clear selection/i })).toBeNull();
  });

  it('clear button calls onClear and does not open the popover', () => {
    const onClear = vi.fn();
    render(
      <AccountCombobox
        accounts={accounts}
        value="a2"
        onChange={vi.fn()}
        onClear={onClear}
        placeholder="Select"
      />,
    );
    fireEvent.click(screen.getByRole('button', { name: /clear selection/i }));
    expect(onClear).toHaveBeenCalledTimes(1);
    // Popover should NOT have opened — none of the option labels is in the DOM.
    expect(screen.queryByText('Checking')).toBeNull();
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
