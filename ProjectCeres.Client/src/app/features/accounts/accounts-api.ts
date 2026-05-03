// ---------- URL builders ----------

export const ACCOUNTS_URL = '/api/accounts';
export const ACCOUNT_BY_ID_URL = (id: string) => `/api/accounts/${id}`;
export const ACCOUNT_ARCHIVE_URL = (id: string) => `/api/accounts/${id}/archive`;
export const ACCOUNT_REACTIVATE_URL = (id: string) => `/api/accounts/${id}/reactivate`;
export const ACCOUNT_LEDGER_URL = (id: string) => `/api/accounts/${id}/ledger`;
export const ACCOUNT_TYPES_URL = '/api/account-types';
export const CURRENCIES_URL = '/api/currencies';

export function buildListUrl(includeInactive: boolean): string {
  return includeInactive ? `${ACCOUNTS_URL}?includeInactive=true` : ACCOUNTS_URL;
}

// ---------- DTOs ----------

export type AccountTypeName = 'Asset' | 'Liability';
export type RepaymentType = 'FullMonthly' | 'Amortising';

export type AccountListItemDto = {
  id: string;
  name: string;
  accountTypeId: number;
  accountTypeName: AccountTypeName;
  currencyId: number;
  currencyCode: string;
  currencySymbol: string;
  description: string | null;
  isActive: boolean;
  excludeFromSpendable: boolean;
  liabilityRepaymentType: RepaymentType | null;
  interestRate: number | null;
  balance: number;
  hasTransactions: boolean;
};

export type AccountDetailDto = {
  id: string;
  name: string;
  accountTypeId: number;
  accountTypeName: AccountTypeName;
  currencyId: number;
  currencyCode: string;
  currencySymbol: string;
  description: string | null;
  isActive: boolean;
  excludeFromSpendable: boolean;
  liabilityRepaymentType: RepaymentType | null;
  interestRate: number | null;
  openingBalance: number;
  openingBalanceDate: string | null;
};

export type AccountTypeDto = {
  id: number;
  name: AccountTypeName;
};

export type CurrencyDto = {
  id: number;
  code: string;
  name: string;
  symbol: string;
};

export type CreateAccountRequest = {
  name: string;
  accountTypeId: number;
  currencyId: number;
  description: string | null;
  openingBalance: number;
  openingBalanceDate: string;
  liabilityRepaymentType: RepaymentType | null;
  interestRate: number | null;
  excludeFromSpendable: boolean;
};

export type UpdateAccountRequest = {
  name: string;
  description: string | null;
  openingBalance: number;
  openingBalanceDate: string;
  liabilityRepaymentType: RepaymentType | null;
  interestRate: number | null;
  excludeFromSpendable: boolean;
};

// ---------- Form values (UI layer) ----------

export type AccountFormValues = {
  name: string;
  accountTypeId: number;
  currencyId: number;
  description: string;
  openingBalance: number;
  openingBalanceDate: string;
  liabilityRepaymentType: RepaymentType | null;
  interestRate: number | null;
  excludeFromSpendable: boolean;
};

// ---------- Ledger DTOs ----------

export type LedgerEntryDto = {
  date: string;
  createdAt: string;
  description: string;
  entryType: string;
  categoryName: string | null;
  signedAmount: number;
  runningBalance: number;
};

export type AccountLedgerDto = {
  accountId: string;
  accountName: string;
  currencySymbol: string;
  entries: LedgerEntryDto[];
};

// ---------- Error envelope ----------

export type ApiErrorEnvelope = {
  error: {
    code: string;
    message: string;
    details: unknown[];
  };
};
