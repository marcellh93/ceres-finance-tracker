# ADR-0068 — PostgreSQL Row-Level Security as Phase 3 Defence in Depth

**Status:** Accepted (Phase 3, Batch 3 — Auth)

**Date:** 2026-05-09

**Supersedes (in part):**
- The Phase 4 deferral of RLS in `security-model.md` § PostgreSQL Row-Level Security and § Required matrix (line referencing "PostgreSQL Row-Level Security policies | Required" at Phase 4).
- The "Phase 4 defense-in-depth layer" framing in ADR-0065 § Rationale and `multi-tenancy-strategy.md` § EF Core Global Query Filters.
- The "PostgreSQL Row-Level Security" item in `roadmap-phase-three.md` Phase-4-begins-with line.

ADR-0065 itself is **not** superseded — its core decision (Option B: global query filters + explicit redundancy + admin-only bypass) remains accepted. Only the line that defers RLS to Phase 4 is overridden.

## Context

Phase 3 ships multi-tenancy via EF Core global query filters (ADR-0065). The defence stack at Phase 3 launch — as originally specified — was:

1. `HasQueryFilter` on every user-owned entity in `ApplicationDbContext.OnModelCreating`.
2. Explicit `.Where(t => t.UserId == _currentUser.UserId)` redundancy in service code.
3. Architecture test that fails the build if `IgnoreQueryFilters()` appears outside `Admin/`.
4. Required IDOR integration tests (User A → User B's resource → 404 for every entity).

ADR-0065 accepted this stack as adequate for Phase 3 and cited "future Phase 4 RLS" as a *named layer of the defence-in-depth stack* in its Rationale section, while simultaneously deferring that layer to Phase 4. The two statements contradict each other: a layered defence cannot rely on a layer that is not built.

The deferral was motivated by three arguments, all weighed and now reconsidered:

**Argument 1 — "Layers 1–4 are sufficient for Phase 3 traffic and surface."** The stack catches LINQ queries, explicit raw SQL with manual `WHERE` clauses, and forgotten admin escapes. It does **not** catch one specific failure mode: `FromSqlRaw` (or other raw-SQL paths) against user-owned tables without an explicit `WHERE UserId = @currentUser` clause. EF query filters do not apply to raw SQL by definition. The architecture test catches `IgnoreQueryFilters()` but not `FromSqlRaw`. Phase 3 ships a Reports module, an Import pipeline, and a Dashboard — three areas where the temptation to drop into raw SQL for performance or for PostgreSQL-specific features (CTEs, window functions, FTS, `LATERAL`) is realistic. The first such call silently bypasses the entire stack.

**Argument 2 — "RLS adds operational complexity disproportionate to the gain."** This was asserted but never measured. A scoped Phase 3 implementation is small: an `IDbCommandInterceptor` that issues `SET LOCAL app.current_user_ref = '<id>'` at the start of every EF command, a single migration adding policies to ~14 user-owned tables, a separate Postgres role with `BYPASSRLS` for `Admin/` and `IUserJobRunner` background work, and per-table integration tests proving `FromSqlRaw` with the wrong user context returns zero rows. This is a few days of work, not the 8–10-week schema-per-tenant migration that Option C (rejected in ADR-0065) would have cost.

**Argument 3 — "Phase 3 is private beta with a small user base; the risk is small."** This argument confused user count with attack surface. The hosted Phase 3 app is reachable from the public internet, runs real authentication, and stores real financial data. The threat model in `security-model.md` explicitly retains "external internet attacker" and "database dump attacker" as in-scope adversaries from Phase 3 onward. A single tenant's data leaking to another tenant during beta is exactly the failure that ends a product before it ships. The "small user base = small risk" framing is also the same logic that defers security work indefinitely. Once a populated production schema exists, retrofitting RLS is materially more expensive than adding it during the cutover that creates the schema in the first place.

The Stage 7 multi-tenancy cutover (the sentinel-to-real-user remap, ADR-0066) is the moment the schema first contains real `AspNetUsers.Id` values — the natural and lowest-cost moment to add RLS policies. Adding them in Phase 4 would require either a no-op cutover (turn policies on against an already-running schema, with the risk that any pre-existing query path that quietly relies on the absence of policies surfaces zero-row regressions in production) or coordinated downtime.

## Decision

PostgreSQL Row-Level Security ships in **Phase 3, immediately after Stage 7**, as a new **Stage 7.5** of the roadmap. RLS is positioned as the **fifth layer** of the defence-in-depth stack, named explicitly in ADR-0065's Rationale and now actually built before Phase 3 launch.

The Phase 3 RLS implementation has five components:

1. **Postgres user roles.** Two roles backed by separate connection strings:
   - `ceres_app` — the application's runtime role. RLS policies apply to it. Used by EF for all user-facing requests and the Razor surface.
   - `ceres_admin` — a separate role with `BYPASSRLS`. Used by Admin services and `IUserJobRunner` cross-tenant background jobs (ADR-0067). DI selects the role-bound `DbContext` via the same mechanism that scopes `ICurrentUserAccessor` (HTTP-context vs. `IUserScope.EnterAs`).

2. **Setting `app.current_user_ref` per command.** An `IDbCommandInterceptor` (`RowLevelSecurityInterceptor`) hooks `ScalarExecuting`, `ReaderExecuting`, and `NonQueryExecuting`. Before each command, it issues `SET LOCAL "app.current_user_ref" = @user_id` against the same connection inside the same transaction. `SET LOCAL` is transaction-scoped — safe with Npgsql connection pooling because it does not leak across transactions. If `ICurrentUserAccessor` cannot resolve a user, the interceptor throws (matches the existing accessor contract).

3. **Policies on every user-owned table.** A single migration `AddRowLevelSecurityPolicies` enables RLS on the 14+ user-owned tables enumerated in ADR-0065 (`Transaction`, `Transfer`, `LiabilityPayment`, `Account`, `Category`, `CategoryBudget`, `Budget`, `RecurringTransaction`, `TransactionAttachment`, `SavedReport`, `UserSession`, `UserBlockedIp`, `Settings`, `SupportTicket`, `AuditLog`, `CsvImportProfile`, `ImportStagedTransaction`, `ImportStagedTransfer`, `ImportTransferExclusion`, and any future user-owned entities). Each table receives:

   ```sql
   ALTER TABLE "Transactions" ENABLE ROW LEVEL SECURITY;
   ALTER TABLE "Transactions" FORCE ROW LEVEL SECURITY;  -- applies even to table owners
   CREATE POLICY user_isolation ON "Transactions"
     USING ("UserId" = current_setting('app.current_user_ref')::uuid)
     WITH CHECK ("UserId" = current_setting('app.current_user_ref')::uuid);
   ```

   The `WITH CHECK` clause means RLS catches inserts/updates that try to write a `UserId` other than the current one — not just reads. System tables (`AccountType`, `CategoryType`, `Currency`, `ReportType`, `SystemCategory`) are not covered.

4. **Migration runs as a third role.** A `ceres_migrator` role (already in scope per `security-model.md` § Database Credentials) holds DDL rights and `BYPASSRLS`. Migrations create policies and seed lookup tables without policy obstruction. The application's runtime role does not have DDL.

5. **Per-table integration tests.** For every user-owned entity:
   - `FromSqlRaw("SELECT * FROM \"Transactions\"")` executed under User B's context returns only User B's rows (proves RLS catches the EF-bypass case).
   - An attempt to `INSERT` a row with a foreign `UserId` raises a Postgres `42501` permission error (proves `WITH CHECK` works).
   - Admin services using the `ceres_admin` role + `IgnoreQueryFilters()` return all rows (proves `BYPASSRLS` works as the documented escape).
   - Background jobs entered via `IUserScope.EnterAs(targetUserId)` see only that user's rows on `ceres_app` (proves the interceptor honours the scope context).

## Rationale

The deciding factor is the same asymmetry that drove ADR-0065: silent failure is more dangerous than loud failure. EF query filters silently fail when bypassed by raw SQL; RLS makes that bypass loud (zero rows or `42501`) at the database level. ADR-0065 chose Option B precisely because its failure mode is loud — RLS extends that property to the one class of failure ADR-0065's stack does not catch.

The retrofit cost asymmetry is real and has been measured in industry — adding RLS to a populated schema requires (a) auditing every existing query path for implicit reliance on no-policy behaviour, (b) scheduling downtime or accepting partial-rollout risk, (c) backfilling test coverage that would have been written naturally during the cutover. Stage 7's sentinel-to-real-user remap is a clean greenfield moment for RLS — the schema's `UserId` columns are being rewritten anyway, so adding policies on top is incremental work, not retrofit work.

The "Phase 4 framing" in `security-model.md` was conventional, not measured. ADR-0065 inherited it and treated it as a design constraint when it was actually an open assumption. This ADR reverses that.

## Consequences

- A new Stage 7.5 lands in `roadmap-phase-three.md` between Stage 7 and Stage 8 with its own verification checklist.
- `security-model.md` § PostgreSQL Row-Level Security is updated: removes the "Phase 4 defense-in-depth layer" framing; replaces with "Phase 3 defence-in-depth layer, ships in Stage 7.5."
- `security-model.md` § Required matrix (the table around line 1025): the "PostgreSQL Row-Level Security policies" row moves from Phase-4-Required to Phase-3-Required.
- `multi-tenancy-strategy.md` § EF Core Global Query Filters: the line "PostgreSQL Row-Level Security in Phase 4 will catch this category at the database level" updates to "PostgreSQL Row-Level Security in Phase 3 (Stage 7.5, ADR-0068) catches this category at the database level."
- `planning-resolved.md` line 57 (the resolved entry for ADR-0065): adds a cross-reference noting that the Phase 4 RLS deferral was overturned by ADR-0068.
- `roadmap-phase-three.md` Phase-4-begins-with line (around line 1537): RLS is removed from the Phase 4 list. Phase 4 retains per-tenant payload encryption, social login, JWT for mobile, and `planning-future.md` items.
- Two additional connection strings are added to configuration: `Postgres__ApplicationConnection` (uses `ceres_app` role) and `Postgres__AdminConnection` (uses `ceres_admin` role). The migrator role uses `Postgres__MigrationConnection` (already specified in `security-model.md` § Database Credentials).
- Phase 4's "per-tenant payload encryption" remains a separate control defending a different threat (database dump attacker). It is not a substitute for RLS, and RLS is not a substitute for it.
- ADR-0065 itself stays accepted — its core "Option B" decision is unaffected. Only the deferral clause is superseded.

## Cross-references

- ADR-0065 — EF Core Global Query Filters with Explicit Redundancy and Admin-Only Bypass (the layered stack this ADR completes)
- ADR-0066 — Sentinel-to-real-user migration (the Stage 7 cutover that immediately precedes RLS adoption)
- ADR-0067 — Background-process user resolution with `IUserScope` and runner (the mechanism that lets cross-tenant jobs satisfy the interceptor)
- `security-model.md` § PostgreSQL Row-Level Security — implementation guidance (will be updated as part of this ADR's adoption)
- `security-model.md` § Database Credentials — three-role separation
- `multi-tenancy-strategy.md` § EF Core Global Query Filters — the multi-tenancy framing this ADR closes the last gap on
- `roadmap-phase-three.md` § Stage 7.5 — the implementation stage
