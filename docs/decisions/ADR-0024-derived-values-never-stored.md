# ADR-0024 — Derived Values Never Stored as Columns

**Status:** Accepted

**Context:**

Several aggregate values — account balance, total assets, total liabilities, net worth, total income, total expenses, net cash flow, and budget actual spend — could be stored as denormalized columns on their respective entities and updated whenever a related transaction or transfer is written.

The alternative is to compute them at query time by aggregating over the underlying transaction and transfer records.

Storing these values as columns creates a second source of truth. Every write path that affects a transaction or transfer (create, edit, delete, transfer between accounts) would need to correctly update every cached total it touches. Any write that misses an update — a forgotten code path, a data migration script, a direct database query — produces a silently incorrect value that is difficult to detect and harder to explain to a user.

## Decision

Never store derived values as columns. Always compute them at query time from source transaction and transfer records.

Affected values:

| Value | Computed from |
|-------|--------------|
| Account balance | SUM of transactions and transfers for that account, sign-adjusted by AccountType |
| Total assets | SUM of balances for all Asset accounts |
| Total liabilities | SUM of balances for all Liability accounts |
| Net worth | Total assets − Total liabilities |
| Total income | SUM of transaction amounts where CategoryType = "Income", scoped by currency and date range |
| Total expenses | SUM of transaction amounts where CategoryType = "Expense", scoped by currency and date range |
| Net cash flow | Total income − Total expenses |
| Budget actual spend | SUM of transaction amounts linked to a Budget |

## Consequences

**Positive:**
- Single source of truth — the value is always consistent with the underlying records
- No cache invalidation logic — writes do not need to maintain any totals
- Deactivated accounts are automatically included in net worth calculations without special handling — the query aggregates over all accounts regardless of active status

**Negative:**
- Balance and net worth queries must aggregate over all transactions for an account or all accounts for a user — for Phase 1/2 with a personal dataset this is acceptable; query performance must be monitored as transaction volume grows in Phase 3+
- Indexes on `(AccountId, Date)` and `(CategoryId, Date)` are required to keep aggregation queries fast — see `models.md → Database Indexes`

**Related decisions:**
- ADR-0007: amounts are always positive; sign convention for asset vs. liability balance calculation is documented in `models.md → Derived Values`
- ADR-0023: deactivated accounts are not excluded from balance calculations — deactivation only affects UI pickers, not aggregations
