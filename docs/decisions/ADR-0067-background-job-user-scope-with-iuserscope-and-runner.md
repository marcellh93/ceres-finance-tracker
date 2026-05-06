# ADR-0067 — Background-Process User Resolution via `IUserScope` and `IUserJobRunner`

**Status:** Accepted (Phase 3, Batch 3 — Auth)

**Date:** 2026-05-07

**Context:**

ADR-0065 establishes that every user-owned EF query is filtered by a global query filter referencing `_currentUser.UserId`. In normal HTTP request flow, `ICurrentUserAccessor` reads the authenticated user from `HttpContextAccessor`. In background work — scheduled jobs, queued jobs, hosted services — there is no `HttpContext`.

Several Phase 3 features require background execution:

- Weekly financial digest email (per-user, opt-in)
- Recurring-transaction reminder emails (per-user, daily)
- Safe-to-Spend alert email (per-user, event-driven)
- New-session-alert email (per-user, event-driven; may be async)
- GDPR data export (per-user, queued — accept request, return 202, generate later)
- Audit-log auto-purge (cross-tenant, scheduled)
- Future Phase 4 jobs: Insight Engine, Projection Engine, Safe-to-Spend forecasting

`multi-tenancy-strategy.md` line 81 flags this as an open hazard: when an `HttpContextAccessor`-backed `ICurrentUserAccessor` runs outside an HTTP request, it resolves `HttpContext` as `null`. Reading `UserId` then either throws or silently falls back to a default like `Guid.Empty`. With ADR-0065's global filter, a default `Guid.Empty` produces `WHERE UserId = '00...0'`, which matches zero rows. The job runs, sends nothing, throws no exception, and the failure goes unnoticed.

The decision is how non-HTTP code obtains a user identity that the global query filter and any user-scoped service can rely on.

The options:

- **Option 1 — `IUserScope.EnterAs(userId)`** returning an `IDisposable`. Background code wraps user-scoped work in `using` blocks; outside any scope, the accessor throws.
- **Option 2 — `IUserJobRunner.ForEachUserAsync(filter, work)`** as a higher-level convenience that handles scope entry/exit and per-user error isolation automatically.
- **Option 3 — A "system user" sentinel** that owns no data and forces every background job to use `IgnoreQueryFilters()`. Rejected: it reintroduces a sentinel concept ADR-0066 just removed, conflates job context with admin access, and provides no real benefit over Option 1.

**Decision:**

Adopt **Option 1 + Option 2**: `IUserScope.EnterAs` as the foundation, `IUserJobRunner.ForEachUserAsync` as a convenience layer for the iteration case. Both ship together in the multi-tenancy cutover release.

1. **`IUserScope`** stores the current user id in `AsyncLocal<Guid?>`. `EnterAs(userId)` returns an `IDisposable` whose `Dispose` clears the value. The scope propagates correctly across `await` boundaries within a single logical flow but does not leak across iterations.

2. **`ICurrentUserAccessor` resolves in a fixed precedence order:**
   1. HTTP context (claim from cookie-authenticated request)
   2. Background scope (the AsyncLocal slot set by `IUserScope.EnterAs`)
   3. Throw `InvalidOperationException` with a clear message: *"No user context available. HTTP requests resolve from cookie; background jobs must enter via IUserScope.EnterAs()."*
   Silent fallback to `Guid.Empty` or any default is forbidden.

3. **`IUserJobRunner.ForEachUserAsync(filter, work)`** wraps the iteration pattern: enumerates users matching `filter` (using `IgnoreQueryFilters()` since this query is intentionally cross-tenant), enters the per-user scope, invokes `work`, exits the scope, and isolates per-user exceptions so one user's failure does not abort the batch. Used by the weekly digest, recurring reminders, and any future per-user fan-out job.

4. **Per-user single-user jobs** (the GDPR export, single-user reminder emails, the new-session alert) use `IUserScope.EnterAs(userId)` directly with the user id from the queued payload. They do not need the iteration runner.

5. **Genuinely cross-tenant background jobs** — audit-log purge, failed-login retention purge, system-table maintenance — do not enter a user scope. They access shared/system tables (which carry no `UserId` and no global filter) or use `IgnoreQueryFilters()` explicitly with documented justification, consistent with the admin-only-bypass rule from ADR-0065.

6. **`Program.cs` boot-time hooks that touch user-owned data are removed.** Specifically, `ISettingsService.EnsureExistsAsync` is no longer called at application startup — Settings rows are created during user registration. Any future boot-time hook that needs user data is a Phase 3 anti-pattern and should be moved into a per-user lifecycle hook (registration, login).

7. **An architecture test attempts to flag any non-HTTP code path** (`IHostedService` implementations, background-job class registrations) that queries user-owned tables without entering a scope. Static enforcement is harder than the `IgnoreQueryFilters()` rule from ADR-0065 — this is best-effort, not a hard build gate.

**Rationale:**

`AsyncLocal<Guid?>` is the standard .NET primitive for ambient-context propagation through async/await. The `using`-block pattern is well-established (it is how ASP.NET Core's `IServiceScope` works) and produces obvious source-level documentation of where user context begins and ends.

Throwing on missing context — rather than returning a default — converts the dangerous silent-failure mode into a loud-failure mode that surfaces immediately the first time a developer forgets the scope. This is the same loud-failure principle ADR-0065 chose for the global query filter.

Building both `IUserScope` and `IUserJobRunner` together (rather than waiting for a second background job to motivate the runner) ensures the patterns set during Phase 3 cutover are the patterns subsequent jobs copy. Refactoring after multiple jobs already exist is more painful than building the right foundation once.

The architecture test is best-effort because Hangfire/Quartz job classes vary in shape, and statically detecting "queries user-owned tables" is approximate. The clear boundary `Admin/` namespace from ADR-0065 is easier to enforce; this one relies on convention plus reviewer attention.

**Consequences:**

- `IUserScope` and `IUserJobRunner` ship in the same release as the multi-tenancy cutover. Order: implement scope primitives → wire `ICurrentUserAccessor` to read from both sources → apply global query filters (ADR-0065) → run sentinel-to-real-user migration (ADR-0066) → background features start landing.
- Every background job in Phase 3 and beyond either runs inside `IUserScope.EnterAs(userId)`, uses `IUserJobRunner.ForEachUserAsync(...)`, or is documented as cross-tenant. There is no fourth option.
- The GDPR data export queues with the requesting user's id in the payload; the worker uses `IUserScope.EnterAs(payload.UserId)` to scope the query layer correctly.
- Connection-pooling concerns for ADR-0065's global filter do not bite here because the AsyncLocal scope rides the logical flow, not the pooled connection.
- `ISettingsService.EnsureExistsAsync` is removed from `Program.cs` startup; per-user Settings rows are created in the registration flow per ADR-0066 § Consequences.
- The `InvalidOperationException` thrown by the accessor on missing context surfaces in logs immediately, allowing the first job that forgets a scope to be fixed before reaching production-impact severity.
- The choice of background-job runtime (Hangfire vs Quartz.NET) is left open; the `IUserScope` mechanism is runtime-agnostic and works with either.

**Cross-references:**

- `multi-tenancy-strategy.md` § Background processes and non-HTTP contexts — original framing of the hazard
- ADR-0065 — Global query filters (the mechanism this decision feeds)
- ADR-0066 — Sentinel-to-real-user migration (the cutover this ships alongside)
- `planning-phase3.md` § Planned Features (weekly digest, recurring reminders, new-session alert, GDPR export — the Phase 3 features that depend on this foundation)
- `security-model.md` § Email Security Rules — per-user rate limiting on email-triggering endpoints, which the runner enforces per iteration
