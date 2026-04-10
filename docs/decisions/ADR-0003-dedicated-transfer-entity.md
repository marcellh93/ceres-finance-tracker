# ADR 0003: Dedicated Transfer Entity for Inter-Account Money Movements

## Status: Accepted

## Context
Users need to record money moving between two accounts they own — for example, moving funds
from a checking account to a savings account, or making a credit card payment from a bank account.

The naive approach would be to record this as two transactions: an expense on the source account
and an income on the destination account. This was considered and rejected because it causes
report inflation: both the total income and total expense figures for a period would increase
by the transfer amount, even though no money was actually earned or spent. The more transfers
a user makes, the more distorted their income and expense reports become.

A transfer is fundamentally neither income nor expense — it is a neutral movement of money
between accounts the user already owns. Net worth does not change. Nothing is gained or lost.

## Decision
A dedicated `Transfer` entity is introduced with a source account, destination account, amount,
date, and optional description. It has no category field. Transfers are excluded from all
income and expense report calculations. A separate Transfer History report exists to make
transfers auditable without contaminating income/expense reports.

Cross-currency transfers are not supported. Both accounts in a transfer must share the same
currency — enforced at the application level. This constraint exists because a cross-currency
transfer would require a conversion rate, which is out of scope (see ADR-0002).

## Consequences

**Positive:**
- Income and expense reports remain accurate — transfer amounts do not inflate either figure
- Net worth calculations are unaffected by transfers, which is correct
- Transfers are fully auditable via their own dedicated report
- The data model clearly expresses the semantic difference between a transaction and a transfer

**Negative:**
- Adds a separate entity and a separate report to the system
- Users must use a different form to record a transfer vs. a transaction — slightly more cognitive load
- Cross-currency transfers are unsupported, which is a real gap for users who move money
  between currency zones (e.g. converting EUR to USD and depositing into a USD account)
