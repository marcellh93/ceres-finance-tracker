# ADR 0013: CreatedAt Column for Intra-Day Transaction and Transfer Ordering

## Status: Accepted

## Context
ADR-0009 established that Transaction and Transfer dates are stored as a `date` type with
no time component. A known consequence documented in that ADR is that two records on the
same day have no defined sequence — within a day, order is undefined.

This becomes a concrete problem when building the Transaction History view: if a user
records multiple transactions on the same day, the display order is arbitrary. Ordering
by ID is fragile and semantically wrong — IDs communicate identity, not time, and break
silently with bulk imports, GUID-based IDs, or record restores.

A dedicated `CreatedAt` column was evaluated against the alternatives documented in
ADR-0009 (adding an optional `Time` column, adding a sequence integer, or accepting
undefined within-day order).

## Decision
Add a `CreatedAt datetime NOT NULL` column to both `Transaction` and `Transfer`.

- Set by the application on insert — never editable by the user
- Used as a tiebreaker when ordering records that share the same `Date`
- Distinct in meaning from `Date`: `Date` is when the financial event occurred
  (user-provided, can be backdated); `CreatedAt` is when the record was entered into
  the system (system-generated, always reflects actual entry time)
- Default sort order for Transaction History and Transfer History:
  ORDER BY Date DESC, CreatedAt DESC

`TransactionAttachment` already has `UploadedAt` which serves the same purpose for
attachments. `SavedReport` already has `CreatedAt`. No other entities require this
column — there is no ordering requirement defined for lookup tables, budgets, or
categories, and GDPR data minimisation principles (see legal.md) discourage adding
columns speculatively.

## Consequences

**Positive:**
- Intra-day ordering is stable and deterministic
- The distinction between event date and entry date is explicit in the schema
- Useful as a lightweight audit trail for when records were created
- Consistent with the existing pattern already used on SavedReport and TransactionAttachment

**Negative:**
- Two new columns to add via migration on existing installations
- Application code must always set CreatedAt on insert — it must not be left to the
  database default unless a server-side default is configured explicitly in EF Core
- CreatedAt reflects the machine clock — if the system clock is wrong, ordering will
  be wrong. Acceptable for a local Phase 1/2 app; revisit if distributed in Phase 3.
