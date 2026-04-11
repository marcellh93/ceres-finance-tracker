# ADR 0006: Decimal Precision for Financial Amounts

## Status: Accepted

## Context
All financial amount fields across the data model (Transaction.Amount, Transfer.Amount,
CategoryBudget.LimitAmount, Budget.TargetAmount) are typed as `decimal`. EF Core via Npgsql maps `decimal` to PostgreSQL's `numeric` type with no precision or scale
by default — unlimited precision, which is an implicit behaviour that is invisible in the
model definition and easy to change accidentally.

In a finance app, the precision of monetary values is a correctness concern. Two decimal
places is the standard for fiat currencies (EUR, USD, GBP, COP, ARS, VED — all supported
currencies use two decimal places). A different precision would either lose data (fewer places)
or store meaningless digits (more places).

The decision needed to be made explicit so that anyone writing a migration or adding a new
amount field applies the same precision consistently.

## Decision
All financial amount columns are declared as `decimal(18,2)`:
- 18 total digits — sufficient for any realistic personal finance figure
- 2 decimal places — matches all supported fiat currencies
- Applied to: `Transaction.Amount`, `Transfer.Amount`, `CategoryBudget.LimitAmount`,
  `Budget.TargetAmount`

This is annotated explicitly in the EF Core model configuration (not left to convention)
so it is visible in code review and migrations.

## Consequences

**Positive:**
- Consistent precision across all financial fields — no silent rounding differences
- Matches the decimal places of all six supported currencies
- Explicit in the schema — visible in generated migrations

**Negative:**
- If a future currency with more than 2 decimal places is added (e.g. some crypto assets
  use 8 decimal places), a migration would be required to widen the precision
- 18 total digits is generous but not infinite — values above 9,999,999,999,999,999.99
  would overflow, which is not a realistic concern for personal finance
