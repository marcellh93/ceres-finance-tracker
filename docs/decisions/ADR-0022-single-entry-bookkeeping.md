# ADR-0022 — Single-Entry Bookkeeping

**Status:** Accepted

**Context:**

Two established approaches exist for modeling financial transactions in software:

- **Double-entry bookkeeping:** every transaction affects two accounts — a debit on one side and a credit on the other. The accounting equation (Assets = Liabilities + Equity) must always balance. Used in formal accounting software (QuickBooks, Xero, SAP).
- **Single-entry bookkeeping:** every financial event is recorded once, against a single account. Direction (income vs. expense) is expressed through categorization, not through paired entries.

The target users for this application are individuals managing personal finances, not accountants. The data model has two distinct concepts: Accounts (what you own or owe) and Categories (what the money is for). Direction is already represented by CategoryType (Income or Expense).

## Decision

Use single-entry bookkeeping. Each transaction belongs to one account and one category. Direction is inferred from the category's type — income transactions increase an asset account; expense transactions decrease it. No debit/credit columns, no journal entries, no chart of accounts.

Double-entry will not be introduced unless customers explicitly request it. The complexity it adds — paired entries, a formal chart of accounts, trial balance reconciliation — is not justified for personal finance tracking and is not how individuals reason about their money.

## Consequences

**Positive:**
- Simple data model that matches how individuals think about money
- No accounting knowledge required to use or maintain the app
- Transfers between accounts are handled by a dedicated `Transfer` entity (ADR-0003) — they are not a debit/credit pair

**Negative:**
- Cannot produce formal double-entry accounting statements (general ledger, trial balance)
- Not suitable as accounting software for a business entity — this is a personal finance tracker, not bookkeeping software

**Related decisions:**
- ADR-0007: amounts are always stored as positive values — direction is derived from the category type, not from a negative sign
- ADR-0003: transfers are a separate entity excluded from income/expense calculations, not a double-entry pair
- ADR-0002: currency scoping and reporting are consistent with a single-entry model
