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

// ---------- Typed CRUD endpoints (Plan 1 server work) ----------

export const TRANSACTION_BY_ID_URL      = (id: string) => `/api/transactions/${id}`;
export const TRANSFER_BY_ID_URL         = (id: string) => `/api/transfers/${id}`;
export const LIABILITY_PAYMENT_BY_ID_URL = (id: string) => `/api/liability-payments/${id}`;

export const MOVEMENT_TYPE_URL = (id: string) => `/api/movements/${id}`;

// ---------- Edit DTOs (responses for GET /:id) ----------

export type TransactionEditDto = {
  id: string;
  date: string;
  amount: number;
  accountId: string;
  categoryId: string;
  description: string | null;
  isCleared: boolean;
  attachments: AttachmentDto[];
};

export type TransferEditDto = {
  id: string;
  date: string;
  amount: number;
  sourceAccountId: string;
  destAccountId: string;
  description: string | null;
  isCleared: boolean;
  attachments: AttachmentDto[];
};

export type LiabilityPaymentEditDto = {
  id: string;
  date: string;
  amount: number;
  assetAccountId: string;
  liabilityAccountId: string;
  description: string | null;
  isCleared: boolean;
};

export type AttachmentDto = {
  id: string;
  fileName: string;
  sizeBytes: number;
  contentType: string;
  uploadedAt: string;
};

export type MovementTypeDto = {
  id: string;
  movementType: MovementType;
};

// ---------- Update request bodies (PUT /:id) ----------

export type UpdateTransactionRequest = {
  date: string;
  amount: number;
  accountId: string;
  categoryId: string;
  description: string | null;
  isCleared: boolean;
  budgetId: string | null;
  needsReview: boolean;
};

export type UpdateTransferRequest = {
  date: string;
  amount: number;
  sourceAccountId: string;
  destAccountId: string;
  description: string | null;
  isCleared: boolean;
};

export type UpdateLiabilityPaymentRequest = {
  date: string;
  amount: number;
  assetAccountId: string;
  liabilityAccountId: string;
  description: string | null;
  isCleared: boolean;
};

// ---------- Project's standard error envelope ----------
// (See docs/api-contract.md — supersedes the ProblemDetails-shaped parser
// used by QuickAddModal for the legacy endpoints.)

export type ApiErrorEnvelope = {
  error: {
    code: string;
    message: string;
    details: Array<{ field?: string; message: string }>;
  };
};
