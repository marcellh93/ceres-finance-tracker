# ADR-0018 — UUID Primary Keys for User-Facing Entities

**Status:** Accepted

**Context:**

All entity primary keys were initially defined as `int`. Integer PKs are sequential and predictable — they appear directly in URLs (`/Transactions/Edit/42`), leaking information about total record counts and activity volume. An attacker can enumerate valid IDs and infer whether a resource exists but is inaccessible (by distinguishing response timing or status codes). This is a meaningful risk for a financial application where any information leakage about a user's data volume is undesirable.

The `UserSession` entity was already designed with a UUID PK for this reason. Using integer PKs elsewhere while UUID is used for sessions creates inconsistency.

This decision is being made before any code is written, which eliminates the need for a data migration later.

**Decision:**

User-created entities use `uuid` as their primary key. System-seeded lookup tables and Settings retain `int` PKs.

| PK type | Entities |
|---------|---------|
| `uuid` | Account, Category, Transaction, Transfer, TransactionAttachment, CategoryBudget, Budget, SavedReport, UserSession, UserBlockedIp |
| `int` | Currency, AccountType, CategoryType, ReportType, Settings |

Lookup tables use `int` because they are system-seeded, never appear in user-facing URLs, and conventional integer IDs are appropriate for reference data.

FK columns follow the referenced table's PK type. For example, `Account.AccountTypeId` stays `int` (points to AccountType), while `Transaction.AccountId` becomes `uuid` (points to Account).

**EF Core implementation:** `Guid` in C# maps to `uuid` in PostgreSQL natively via Npgsql. UUIDs are generated client-side on insert via `Guid.NewGuid()` — no database sequence or trigger required.

When a user requests a resource they do not own, the response must always be `404`, not `403`. Returning `403` confirms the resource exists, which partially defeats the purpose of using UUIDs.

**Consequences:**

- UUID-based URLs prevent sequential enumeration — an attacker gains no information about record counts or activity from a valid resource ID
- Consistent with the UserSession entity which was already UUID
- 16 bytes per UUID vs 4 bytes for int — no performance concern at personal finance app scale
- Random UUIDs cause some B-tree index fragmentation; negligible at this scale and dwarfed by the security benefit
- Slightly more verbose in query logs and manual database inspection compared to short integers
