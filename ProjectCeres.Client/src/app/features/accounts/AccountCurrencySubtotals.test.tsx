import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AccountCurrencySubtotals } from './AccountCurrencySubtotals';
import type { AccountListItemDto } from './accounts-api';

function row(over: Partial<AccountListItemDto>): AccountListItemDto {
  return {
    id: 'x', name: 'X', accountTypeId: 1, accountTypeName: 'Asset',
    currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
    description: null, isActive: true, excludeFromSpendable: false,
    liabilityRepaymentType: null, interestRate: null,
    balance: 0, hasTransactions: false,
    ...over,
  };
}

describe('AccountCurrencySubtotals', () => {
  it('renders nothing when accounts span only one currency', () => {
    const rows = [
      row({ id: 'a', balance: 1000, currencyCode: 'EUR' }),
      row({ id: 'b', balance: 500, currencyCode: 'EUR' }),
    ];
    const { container } = render(<AccountCurrencySubtotals rows={rows} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders one entry per currency when 2+ currencies are present', () => {
    const rows = [
      row({ id: 'a', balance: 1000, currencyCode: 'EUR', currencySymbol: '€' }),
      row({ id: 'b', balance: 500,  currencyCode: 'USD', currencySymbol: '$' }),
    ];
    render(<AccountCurrencySubtotals rows={rows} />);
    expect(screen.getByText(/EUR/)).toBeInTheDocument();
    expect(screen.getByText(/USD/)).toBeInTheDocument();
  });

  it('subtracts liability balances from asset balances per currency', () => {
    const rows = [
      row({ id: 'a', balance: 1000, accountTypeName: 'Asset',     currencyCode: 'EUR', currencySymbol: '€' }),
      row({ id: 'b', balance: 300,  accountTypeName: 'Liability', currencyCode: 'EUR', currencySymbol: '€' }),
      row({ id: 'c', balance: 200,  accountTypeName: 'Asset',     currencyCode: 'USD', currencySymbol: '$' }),
    ];
    render(<AccountCurrencySubtotals rows={rows} />);
    expect(screen.getByText(/€700/)).toBeInTheDocument();
    expect(screen.getByText(/\$200/)).toBeInTheDocument();
  });

  it('includes archived accounts in the math (strip reflects what the parent passes)', () => {
    const rows = [
      row({ id: 'a', balance: 1000, currencyCode: 'EUR', currencySymbol: '€' }),
      row({ id: 'b', balance: 500, currencyCode: 'USD', currencySymbol: '$', isActive: false }),
      row({ id: 'c', balance: 200, currencyCode: 'USD', currencySymbol: '$' }),
    ];
    render(<AccountCurrencySubtotals rows={rows} />);
    expect(screen.getByText(/\$700/)).toBeInTheDocument();
  });
});
