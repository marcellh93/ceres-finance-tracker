# ADR-0066 — Sentinel-to-Real-User Migration: Remap to First Registered User

**Status:** Accepted (Phase 3, Batch 3 — Auth)

**Date:** 2026-05-07

**Context:**

Phase 1 and Phase 2 ran without authentication. The `SingleUserAccessor` returns a fixed sentinel UUID (`00000000-0000-0000-0000-000000000001`) for every request, and every user-owned row in the database is tagged with that sentinel. The single-row `Settings` table is also tagged with the sentinel.

Phase 3 introduces real authentication and multi-tenancy. At cutover, every user-owned row carries a sentinel that no longer corresponds to any real user, and the `UserId` column needs to point at a real `AspNetUsers.Id`.

`multi-tenancy-strategy.md` § Settings Table Migration outlines two options for what happens to the existing data:

1. Remap the sentinel rows to the first registered user
2. Delete the sentinel data; every user (including the developer) starts empty

The doc favors option 2 ("cleaner — avoids orphaned rows and ties defaults to the registration flow") on the assumption that the sentinel data is test fixtures.

In practice, the sentinel rows contain real personal-finance history accumulated during Phase 1 and Phase 2 development — months of transactions, accounts, configured budgets, recurring rules, and import-deduplication state used to validate the platform. Discarding it represents real data loss, not a fixture wipe.

Phase 3 launches as **invite-only beta** with the developer as the first invitee, which removes the principal risk that "remap to first user" was meant to mitigate (a stranger registering before the developer and inheriting the data).

**Decision:**

Adopt **Option 1: remap the sentinel UUID to the first registered user.**

When the first real user completes registration, a one-shot migration runs inside a single PostgreSQL transaction:

1. **Pre-checks (defensive):**
   - Exactly one user row exists in `AspNetUsers` (the user who just registered)
   - At least one row tagged with the sentinel UUID exists in user-owned tables
   - If either check fails, abort the migration and roll back

2. **Remap step:**
   `UPDATE <table> SET user_id = :first_real_user_id WHERE user_id = :sentinel`
   applied to every user-owned table: `accounts`, `transactions`, `transfers`, `liability_payments`, `categories`, `category_budgets`, `budgets`, `recurring_transactions`, `transaction_attachments`, `transfer_attachments`, `saved_reports`, `settings`, and any other user-owned tables that exist at cutover time. (`UserSession`, `SupportTicket`, `AuditLog` start empty in Phase 3 — they have no sentinel rows.)

3. **Post-check (verification):**
   `SELECT COUNT(*) FROM <every user-owned table> WHERE user_id = :sentinel` returns 0 across all tables. If any sentinel row remains, the transaction is rolled back.

4. **Commit.**

5. **Codebase cleanup in the same release:**
   - The sentinel UUID constant is removed from `SingleUserAccessor` and the class itself is deleted (replaced by the real `HttpContextAccessor`-backed `ICurrentUserAccessor` from ADR-0067).
   - Seed scripts and tests that reference the sentinel are updated to use real user fixtures.
   - The migration is also gated to run **only once** — a flag on the migration itself or a check against the schema version prevents accidental re-runs.

**Rationale:**

The sentinel data is the developer's real financial history, not synthetic test fixtures. Preserving it means launch-day Phase 3 has realistic data for end-to-end validation (reports against real transaction history, import deduplication against real bank-CSV state, budget alerts against real spending patterns), and the developer's personal Project Ceres usage is uninterrupted.

The risk that justified the planning doc's preference for option 2 (a stranger inheriting the data) does not apply: Phase 3 launches invite-only with the developer as first invitee. The risk window is effectively zero.

The "cleaner" benefit of option 2 — avoiding orphaned rows — is one-shot. The data loss is permanent. The asymmetry favors preservation.

The first-run / onboarding experience can still be tested by registering a second user (a throwaway email) and going through onboarding fresh; Option 1 does not block that test path.

**Consequences:**

- The first registered user must be the developer. The Phase 3 invite-only launch makes this enforceable; no UI exposes registration before the developer's first invite.
- The Settings row migrates with the rest. The developer's existing currency, period start day, number format, and date format become the first user's preferences automatically.
- The migration is irreversible after commit. A pre-migration database backup is mandatory; the deployment runbook for Phase 3 cutover must include a snapshot step.
- The sentinel constant disappears from the codebase in the same release. Any code path that still depends on it after cutover is a bug, not a fallback.
- Subsequent users (every registration after the first) get a default Settings row created by the registration flow, seeded with system defaults — this is the path documented in `multi-tenancy-strategy.md` § Settings Table Migration step 4 and applies regardless of which option was chosen for the first-user case.
- Multi-user isolation tests use synthetic test users in the test database, not the production migration. The choice here does not affect the IDOR test fixtures.
- A Phase 3 GDPR erasure flow (separate scope) provides the developer with a future "wipe and start fresh" path if ever needed — option 1 is not an irreversible commitment to keeping the data.

**Cross-references:**

- `multi-tenancy-strategy.md` § Settings Table Migration — original framing
- ADR-0065 — EF global query filters (the filters activate against the remapped UserId)
- ADR-0067 — Background-process user resolution (the broader cutover this migration is part of)
- `planning-phase3.md` § Planned Features — invite-only launch model that justifies the risk profile
