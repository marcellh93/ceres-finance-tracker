# ADR 0011: System Categories with IsSystem Flag

## Status: Accepted

## Context
ADR-0010 introduces an "Opening Balance" category that is created by the app and required
for correct operation. ADR-0005 established that all seeded starter categories are editable
and deletable by the user.

These two requirements are now in conflict: if the "Opening Balance" category can be renamed
or deleted, report queries that exclude it by name or ID will silently break.

A mechanism is needed to distinguish app-required categories from user-managed categories.

## Decision
Add an `IsSystem bit NOT NULL` column to the `Category` table.

- `IsSystem = true` marks a category as seeded and required by the application
- System categories cannot be renamed, deactivated, or deleted by the user
- The UI must hide edit and delete controls for any category where `IsSystem = true`
- System categories are included in transaction history and balance queries as normal
- System categories are **excluded** from income/expense report totals
- Currently, exactly one system category exists: "Opening Balance" (Income type)

All user-created categories and the non-system seeded starter categories (ADR-0005) have
`IsSystem = false` and remain fully editable.

## Consequences

**Positive:**
- Application logic that depends on specific categories has a stable, queryable contract:
  `WHERE IsSystem = true` rather than hard-coding a category name string
- Users cannot accidentally break the opening balance feature by renaming or deleting its
  category
- The pattern extends cleanly if future phases need additional system categories

**Negative:**
- A new column must be added to `Category` — requires a migration on existing installations
- Existing seeded categories need `IsSystem = false` in the seed data to be explicit
- The report exclusion rule (`WHERE IsSystem = false`) must be applied consistently in every
  income/expense query — a missing filter is a silent miscalculation
- Users may find it confusing that one category in the list has no edit or delete button
  — the UI should display a tooltip or note explaining that system categories are required
  by the application
