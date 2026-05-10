# ADR-0065 — EF Core Global Query Filters with Explicit Redundancy and Admin-Only Bypass

**Status:** Accepted (Phase 3, Batch 3 — Auth)

**Date:** 2026-05-07

**Context:**

Phase 3 introduces multi-tenancy: every user-owned row is scoped to a `UserId`, and every query against user-owned tables must be filtered by the authenticated user's id. A forgotten `UserId` filter on a single endpoint produces silent cross-tenant data leakage, which is the most catastrophic bug class in a multi-tenant app.

`multi-tenancy-strategy.md` § EF Core Global Query Filters frames the choice but defers it to a Phase 3 decision and notes: *"if used, they must be set up before any multi-user service code is written — retrofitting is harder than applying from the start."*

The options are:

- **A. Manual filtering only** — every query in every service explicitly writes `.Where(t => t.UserId == currentUserId)`. Solo developer; no second pair of eyes; probability of forgetting once over the lifetime of the project approaches certainty. Failure mode is silent.
- **B. Global filters + explicit redundancy** — `HasQueryFilter` on every user-owned entity adds the predicate automatically; service code also writes the explicit `.Where()` for documentation and intent. Failure mode is loud (a forgotten admin escape returns no rows, immediately noticed).
- **C. Schema-per-tenant or database-per-tenant** — physical isolation. Considered and rejected for Phase 3 (operational cost dominates the security gain at beta scale; admin tooling, migrations, connection pooling, backups, and analytics all multiply by N).

PostgreSQL Row-Level Security is treated as a Phase 4 defense-in-depth layer (per `security-model.md` § Row-Level Security), not a Phase 3 launch gate.

**Decision:**

Adopt **Option B: global query filters + explicit redundancy + admin-only bypass.**

1. **Global query filters** are applied to every user-owned entity in `ApplicationDbContext.OnModelCreating`:
   `Transaction`, `Transfer`, `LiabilityPayment`, `Account`, `Category`, `CategoryBudget`, `Budget`, `RecurringTransaction`, `TransactionAttachment`, `SavedReport`, `UserSession`, `Settings`, `SupportTicket`, `AuditLog`, and any future user-owned entities. System tables (`AccountType`, `CategoryType`, `Currency`, `ReportType`, `SystemCategory`) receive no filter. **Intentionally cross-tenant entities** that record events spanning unknown or non-existent users — `FailedLoginAttempt` (added Stage 6b.2) and any future security-event log — also receive no filter; they are read by background purge jobs per ADR-0067 § Decision-6.

2. **Service code continues to write `.Where(t => t.UserId == _currentUser.UserId)` explicitly** even though the global filter would also catch it. The redundancy documents intent at the call site and provides a second layer that is grep-able during code review.

3. **`IgnoreQueryFilters()` is reserved for the `Admin/` namespace.** Admin services that need cross-user visibility (platform stats, user lookup, GDPR erasure) call `IgnoreQueryFilters()` explicitly and pair it with the appropriate scoping (`.Where(t => t.UserId == targetUserId)` for per-user admin queries; no scoping for true platform aggregates).

4. **An architecture test fails the build** if `IgnoreQueryFilters()` appears in any namespace other than `Admin/`. The test is the structural enforcement of rule (3).

5. **Phase 3 admin surface is metadata-only** — counts, user status, lockout, suspension, audit log access. **No transaction-level data viewing and no user impersonation in Phase 3.** Impersonation is deferred until the audit infrastructure and consent layer exist (Phase 4+).

6. **The first user (the developer) receives the Admin role via seed migration.** No UI grants the Admin role.

**Rationale:**

The asymmetry of failure modes is the deciding factor. Option A's failure is silent (page works, query returns wrong data, nobody notices); Option B's failure is loud (admin code without `IgnoreQueryFilters()` returns zero rows and is fixed immediately). In a solo-developer context with no second reviewer, choosing the loud-failure architecture is a structural safety advantage that does not depend on perfect human attention.

The retrofit penalty is real: applying global filters before any multi-user code is written is dramatically cheaper than retrofitting them later. Phase 3 is the moment the decision must be made, and it cannot be revisited cheaply.

Schema-per-tenant (Option C) was considered. It eliminates the leak class architecturally but adds 8–10 weeks of migration work, an ongoing migration-fan-out tax on every schema change, connection-pooling complexity, and a separate admin-tooling burden. The trigger for that migration is a business reason (enterprise customer, regulatory contract) rather than a defensive one. The defensive case is met by the layered Option B stack: global filter + explicit `.Where()` + integration tests + architecture test + future Phase 4 RLS + future Phase 4 per-tenant payload encryption.

**Consequences:**

- `ICurrentUserAccessor` is injected into `ApplicationDbContext` so the global filter expression can resolve the current user. The accessor reads from the HTTP context first, then from the background scope (ADR-0067), then throws.
- Raw SQL queries against user-owned tables are not subject to global filters. Either avoid raw SQL on user-owned tables, or always include the explicit `WHERE UserId` clause. Phase 4 RLS will catch this category at the database level.
- Migrations to add new user-owned entities must remember to add `HasQueryFilter` for the entity. A code-review checklist item enforces this.
- Cross-user queries (weekly digest fan-out, audit-log purge) live in `Admin/` or are explicitly cross-tenant background jobs (ADR-0067). They use `IgnoreQueryFilters()` with documented justification.
- Integration tests required before Phase 3 launch (`multi-tenancy-strategy.md` § Required Integration Tests) are mandatory: User A → User B's resource → 404, not 403, for every user-owned entity.
- `multi-tenancy-strategy.md` § EF Core Global Query Filters is updated to reflect that filters are adopted (not optional), pending the doc-sync pass.

**Cross-references:**

- `multi-tenancy-strategy.md` § EF Core Global Query Filters — original framing of the choice
- `multi-tenancy-strategy.md` § Required Integration Tests — IDOR test gate
- `security-model.md` § IDOR Prevention — controller-side enforcement rule
- `security-model.md` § PostgreSQL Row-Level Security — Phase 4 defense-in-depth layer
- ADR-0066 — Sentinel-to-real-user migration (the cutover that activates these filters)
- ADR-0067 — Background-process user resolution (the mechanism that lets non-HTTP code satisfy the filter)
- ADR-0028 — Admin dashboard architecture (admin surface this decision constrains)
