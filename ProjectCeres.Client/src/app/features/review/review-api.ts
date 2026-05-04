// URL builders
export const RECONCILIATION_REVIEW_PENDING_URL       = '/api/reconciliation-review/pending';
export const RECONCILIATION_REVIEW_PENDING_COUNT_URL = '/api/reconciliation-review/pending/count';
export const RECONCILIATION_REVIEW_CONFIRM_URL       = (id: string) => `/api/reconciliation-review/${id}/confirm`;
export const RECONCILIATION_REVIEW_CONFIRM_ALL_URL   = '/api/reconciliation-review/confirm-all';
export const RECONCILIATION_REVIEW_DISPUTE_URL       = (id: string) => `/api/reconciliation-review/${id}/dispute`;

export const TRANSFER_REVIEW_PENDING_URL       = '/api/transfer-review/pending';
export const TRANSFER_REVIEW_PENDING_COUNT_URL = '/api/transfer-review/pending/count';
export const TRANSFER_REVIEW_LINK_URL          = (id: string) => `/api/transfer-review/${id}/link-to-existing`;
export const TRANSFER_REVIEW_CREATE_URL        = (id: string) => `/api/transfer-review/${id}/create-as-transfer`;
export const TRANSFER_REVIEW_DISMISS_URL       = (id: string) => `/api/transfer-review/${id}/dismiss-as-transaction`;

// DTO types — mirrors of the C# records in ReconciliationReviewApiDtos.cs and TransferReviewApiDtos.cs
export type StagedTransactionDto = {
  id: string;
  importedAt: string;          // ISO 8601 from server
  accountId: string;
  accountName: string;
  accountCurrencyCode: string;
  accountCurrencySymbol: string;
  rawDate: string;             // ISO date (YYYY-MM-DD) from server DateOnly
  rawAmount: number;
  rawDescription: string | null;
  matchedTransactionId: string | null;
  matchedTransactionDescription: string | null;
  matchedTransactionDate: string;
  matchedTransactionAmount: number;
};

export type StagedTransferDto = {
  id: string;
  importedAt: string;
  accountId: string;
  accountName: string;
  accountCurrencyCode: string;
  accountCurrencySymbol: string;
  rawDate: string;
  rawAmount: number;
  rawDescription: string | null;
  candidateTransactionId: string | null;
  candidateTransactionDescription: string | null;
  candidateTransactionDate: string | null;
  candidateTransactionAmount: number | null;
};

// Wire shape for Link / Create dialog submit
export type TransferReviewActionRequest = {
  otherAccountId: string;
};
