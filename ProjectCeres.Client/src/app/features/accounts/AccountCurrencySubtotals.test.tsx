import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AccountCurrencySubtotals } from './AccountCurrencySubtotals';
import type { AccountListItemDto } from './accounts-api';

function row(over: Partial<AccountListItemDto>): AccountListItemDto {
  return {
    id: 'x', name: 'X', accountTypeId: 1, accountTypeName: 'Asset',
    currencyId: 1, currencyCode: 'EUR', currencySymbol: '€',
    description: null, isActive: true, excludeFromSpendable: false, excludeFromReports: false,
    liabilityRepaymentType: null, interestRate: null,
    balance: 0, hasTransactions: false,
    ...over,
  };
}

describe('AccountCurrencySubtotals', () => {
  it('renders nothing when there are no accounts', () => {
    const { container } = render(<AccountCurrencySubtotals rows={[]} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders a single entry when accounts span only one currency', () => {
    const rows = [
      row({ id: 'a', balance: 1000, currencyCode: 'EUR', currencySymbol: '€' }),
      row({ id: 'b', balance: 500,  currencyCode: 'EUR', currencySymbol: '€' }),
    ];
    render(<AccountCurrencySubtotals rows={rows} />);
    expect(screen.getByText(/EUR/)).toBeInTheDocument();
    expect(screen.getByText(/€1,500\.00/)).toBeInTheDocument();
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

  it('excludes accounts flagged ExcludeFromReports from the math', () => {
    const rows = [
      row({ id: 'a', balance: 4102.85, accountTypeName: 'Asset',     currencyCode: 'EUR', currencySymbol: '€' }),
      row({ id: 'b', balance: 154.95,  accountTypeName: 'Liability', currencyCode: 'EUR', currencySymbol: '€' }),
      row({ id: 'c', balance: 5000,    accountTypeName: 'Liability', currencyCode: 'EUR', currencySymbol: '€', isActive: false, excludeFromReports: true }),
      row({ id: 'd', balance: 200,     accountTypeName: 'Asset',     currencyCode: 'USD', currencySymbol: '$' }),
    ];
    render(<AccountCurrencySubtotals rows={rows} />);
    // EUR net should be 4102.85 - 154.95 = 3947.90 (Test Loan excluded), not -1052.10.
    expect(screen.getByText(/€3,947\.90/)).toBeInTheDocument();
    expect(screen.queryByText(/-€1,052/)).toBeNull();
  });
});
