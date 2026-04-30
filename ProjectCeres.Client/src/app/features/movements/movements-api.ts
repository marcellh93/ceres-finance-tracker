export const MOVEMENTS_URL                     = '/api/movements';
export const MOVEMENTS_CLEARED_URL             = (id: string) => `/api/movements/${id}/cleared`;
export const TRANSACTIONS_CREATE_URL           = '/api/transactions';
export const TRANSFERS_CREATE_URL              = '/api/transfers';
export const LIABILITY_PAYMENTS_CREATE_URL     = '/api/liability-payments';
export const ACCOUNTS_ACTIVE_URL               = '/api/accounts/active';
export const CATEGORIES_ACTIVE_URL             = '/api/categories/active';

export type MovementType = 'Transaction' | 'Transfer' | 'LiabilityPayment';

export type MovementListItemDto = {
  id: string;
  movementType: MovementType;
  date: string; // "yyyy-MM-dd"
  amount: number;
  currencyCode: string;
  currencySymbol: string;
  description: string | null;
  isCleared: boolean;
  accountName: string | null;
  categoryName: string | null;
  categoryTypeName: string | null;
  sourceAccountName: string | null;
  destAccountName: string | null;
  assetAccountName: string | null;
  liabilityAccountName: string | null;
};

export type MovementsPageDto = {
  items: MovementListItemDto[];
  totalCount: number;
  page: number;
  pageSize: number;
};

export type CreateTransactionRequest = {
  date: string;
  amount: number;
  accountId: string;
  categoryId: string;
  description: string | null;
};

export type CreateTransferRequest = {
  date: string;
  amount: number;
  sourceAccountId: string;
  destAccountId: string;
  description: string | null;
};

export type CreateLiabilityPaymentRequest = {
  date: string;
  amount: number;
  assetAccountId: string;
  liabilityAccountId: string;
  description: string | null;
};

export type AccountOptionDto = {
  id: string;
  name: string;
  currencyCode: string;
  currencySymbol: string;
  accountTypeName: string;
};

export type CategoryOptionDto = {
  id: string;
  name: string;
  categoryTypeName: string;
};

export type ServerValidationProblem = {
  errors?: Record<string, string[]>;
};
