# ADR 0010: Opening Balance as Auto-Created Transaction on Account Creation

## Status: Accepted

## Context
Every account needs a starting point: the balance it held when the user first began tracking
it in the app. There are two ways to represent this:

1. **Stored column (`OpeningBalance decimal`):** Store the starting balance directly on the
   Account record. Simple to read back, but creates a hybrid model where some of the account
   balance comes from a column and the rest from transactions. Any query that computes a
   running balance must special-case this column.

2. **Opening balance transaction:** Create a real transaction at account-creation time, tagged
   to a designated system category. Account balance is then always the sum of all transactions
   — no special-casing required.

A core invariant of the data model is that account balance is always derived from transactions
(see CLAUDE.md — "Account balance is always derived, never stored as a column"). A stored
`OpeningBalance` column would violate this invariant.

## Decision
Account balance remains 100% derived from transactions. When a user creates an account and
enters a starting balance, the application automatically creates an opening balance transaction
for that amount, tagged to the system "Opening Balance" category.

- The account creation form includes a starting balance field (defaults to zero)
- On save, the app creates the transaction without requiring the user to do so manually
- The transaction is visible in transaction history, labeled as the opening entry
- The transaction is editable if the user entered the wrong amount
- If the starting balance is zero, no transaction is created (or a zero-amount transaction
  is created — implementation choice, but must not affect balance calculations)

The "Opening Balance" category is Income type, seeded by the app, and has `IsSystem = true`
(see ADR-0011). It is excluded from all income/expense report totals.

## Consequences

**Positive:**
- Account balance is always computed by the same query: `SUM(Amount) WHERE AccountId = X`
  — no conditional logic for opening balance
- Running balance in transaction history works correctly from the very first entry
- No special column to forget to include in balance calculations
- The opening balance is auditable and correctable like any other transaction

**Negative:**
- The implicit transaction surprises users who inspect the database directly — they will see
  a transaction they did not explicitly create
- If a user deletes the opening balance transaction, the account balance will be wrong with
  no obvious explanation — the UI should warn before allowing this deletion
- The "Opening Balance" category must be excluded from income reports in every report query;
  a missing exclusion is a silent calculation error
