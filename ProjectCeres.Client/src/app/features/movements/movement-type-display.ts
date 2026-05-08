import type { MovementType, MovementListItemDto } from './movements-api';

/**
 * User-facing labels for movement types.
 *
 * The internal enum values (`Transaction`, `Transfer`, `LiabilityPayment`)
 * are stable across the data model, the database, and the API. The labels
 * here are what the user actually sees — keep them in one place so we
 * never have to chase strings across the codebase when a name changes.
 *
 * Singular form: dropdowns, headings, badges (e.g., "New Debt Payment").
 * Plural form: filters and counts (e.g., "Debt Payments").
 * Verb form: contextual descriptions ("pay down a debt", "transfer money").
 */

export const MOVEMENT_TYPE_LABEL: Record<MovementType, string> = {
  Transaction: 'Transaction',
  Transfer: 'Transfer',
  LiabilityPayment: 'Debt Payment',
};

export const MOVEMENT_TYPE_LABEL_PLURAL: Record<MovementType, string> = {
  Transaction: 'Transactions',
  Transfer: 'Transfers',
  LiabilityPayment: 'Debt Payments',
};

/**
 * Lower-case noun used inside sentences ("New transaction", "Edit transfer",
 * "Edit debt payment"). Don't capitalize the first letter — let the caller
 * decide based on context.
 */
export const MOVEMENT_TYPE_NOUN: Record<MovementType, string> = {
  Transaction: 'transaction',
  Transfer: 'transfer',
  LiabilityPayment: 'debt payment',
};

/**
 * One-line description shown as a tooltip on the +New dropdown items
 * and as helper text below the form heading. Plain language, no jargon.
 */
export const MOVEMENT_TYPE_HINT: Record<MovementType, string> = {
  Transaction:
    'Income or expense — money coming in or going out (e.g., paying a bill, receiving salary).',
  Transfer:
    'Move money between your own accounts (e.g., ATM withdrawal, transfer between checking and savings).',
  LiabilityPayment:
    'Pay down a credit card, loan, or mortgage.',
};

/**
 * Form-level helper text shown below the heading on Create / Edit pages.
 * Slightly longer than the dropdown hint — assumes the user already knows
 * which type they picked and wants a concrete example.
 */
export const MOVEMENT_TYPE_FORM_HELPER: Record<MovementType, string> = {
  Transaction:
    'A single income or expense — paying for groceries, receiving salary, a refund.',
  Transfer:
    'Move money between your own accounts. ATM withdrawals, cash deposits, transfers between checking and savings.',
  LiabilityPayment:
    'Use this when you pay a credit card bill, mortgage, loan, or any account you owe.',
};

/**
 * Tailwind text-color class for a movement's amount in lists / cards.
 *
 * - Transaction Income → success (green)
 * - Transaction Expense → destructive (red)
 * - Transaction with no category type → foreground neutral
 * - Transfer → chart-6 token
 * - LiabilityPayment → chart-7 token
 */
export function amountColor(item: MovementListItemDto): string {
  if (item.movementType === 'Transaction') {
    if (item.categoryTypeName === 'Income') return 'text-success';
    if (item.categoryTypeName === 'Expense') return 'text-destructive';
    return 'text-foreground';
  }
  if (item.movementType === 'Transfer') return 'text-chart-6';
  if (item.movementType === 'LiabilityPayment') return 'text-chart-7';
  return 'text-foreground';
}
