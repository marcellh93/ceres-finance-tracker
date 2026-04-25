# ADR 0038: ImportService Test Approach

## Status: Superseded by ADR-0059

## Context

CSV import is one of the more complex features in Phase 2. `ImportService` performs several
distinct steps in sequence:

1. **Parse** — reads raw CSV text, maps columns to app fields
2. **Sign-flip** — bank exports represent debits as negative numbers; the importer makes them positive
3. **Category inference** — maps a CSV category name to an app `Category` record
4. **Duplicate detection** — fingerprint matching against existing transactions (see ADR-0039)
5. **Multi-row write** — inserts each valid row as a `Transaction`
6. **Error accumulation** — collects rows that fail without stopping the whole import

Steps 1, 2, and 6 are pure logic with no database dependency. Steps 3, 4, and 5 require
the database. Three test approaches were considered:

- **Unit tests only** — fast and isolated, but leaves database steps untested
- **Integration tests only** — catches everything but harder to pinpoint failures
- **Both** — each layer tests what it is actually responsible for

## Decision

**Both unit and integration tests (Option C).**

### Unit tests — pure logic in isolation

Cover steps 1, 2, and 6 using in-memory CSV strings. No database, no DI container.

Scenarios covered:
- Valid rows parsed correctly into import DTOs
- Negative debit amounts flipped to positive
- Missing required fields accumulated as row errors
- Rows with unparseable amounts accumulated as row errors
- Empty CSV handled gracefully (zero rows, zero errors)

### Integration tests — full import path

Cover steps 3, 4, and 5 against the real test database. Two scenarios minimum:

- **Happy path** — all rows valid, all inserted, correct `IsCleared` state per ADR-0039
- **Partial failure** — some rows succeed, some fail (unknown category, bad amount);
  successful rows are inserted, failed rows are returned with reasons

### File format enforcement

Only CSV is accepted. XLSX and other formats are rejected before parsing with a user-facing
error message:

> "This file format is not supported. Please export your transactions as a CSV file from
> your bank. Most banks offer this under Download → CSV or Export → CSV."

No silent rejection. No XLSX parsing library introduced in Phase 2. XLSX support is
deferred to Phase 3 if daily use shows it is a genuine pain point.

### Fixture files

Sample CSV files are checked into the test project and used by both unit and integration
tests. They are written by hand — they do not depend on real financial data.

```
ProjectCeres.Tests/
  Fixtures/
    import_happy_path.csv              ← 5 valid rows, mixed income and expense
    import_partial_failure.csv         ← 3 valid rows, 2 invalid
    import_negative_debits.csv         ← bank-style negative amounts requiring sign flip
    import_duplicate_candidate.csv     ← rows that match existing test database transactions
```

## Consequences

**Positive:**
- Pure logic (parsing, sign-flip, error accumulation) is tested fast and precisely in isolation
- Full import path (category inference, duplicate detection, multi-row write) is verified
  against a real database
- Fixture files make test scenarios reproducible and independent of real user data
- XLSX rejection message reduces support friction without requiring a parsing library

**Negative:**
- Two test layers to maintain — if the import pipeline changes, both unit and integration
  tests may need updating
- Fixture files must be kept in sync with the CSV column mapping UX decision (open question)
  — if column mapping changes, fixtures change too

## Superseded By

ADR-0059 (Import Parser Abstraction and Multi-Format Support) supersedes the file format
enforcement section of this ADR. The "XLSX and other formats are rejected" decision has
been reversed — XLSX is now supported. The test approach (unit + integration) is unchanged.
