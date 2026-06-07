# Stage 9.5d — AppRole test suite (RLS-active auth write-path verification)

**Status:** Approved (brainstorm 2026-06-07). Supersedes the roadmap's shorthand "test project" wording with a collection-based shape (see D1 below).

**Goal:** Add a focused set of integration tests that exercise the real authentication write-flows connected as the *restricted* `ceres_app` database role (the role Row-Level Security constrains), proving the RLS wall holds for those writes — a property the existing ~60 admin-context auth tests never check, because they run as `ceres_admin` (BYPASSRLS).

**Roadmap line:** `docs/roadmap-phase-three.md` line 1255 (Stage 9.5d). Prerequisite 9.5k satisfied; builds directly on 9.5b's `DualContextWebApplicationFactory`.

---

## 1. Background — why this stage exists

Stage 7.5 installed PostgreSQL Row-Level Security on every user-owned table: the runtime role `ceres_app` (NOBYPASSRLS) can only see rows matching the `app.current_user_ref` GUC. But the entire integration suite routes through `ceres_admin` (BYPASSRLS) via `WafCollection.cs`'s `ApplicationConnection` override — so the RLS policies are *silently inert* in tests. The `EmailConfirmationTokens` gap (Stage 9.3) shipped exactly because of this: a missing RLS policy was invisible to a green admin-context suite.

9.5b shipped the capability to fix this (`DualContextWebApplicationFactory` boots the app wired to `ceres_app`; `RlsParityStartupCheck` fail-closes at boot). 9.5d *uses* that capability: it adds an auth-flow test layer that runs under `ceres_app`, so a missing or wrong RLS policy on an auth-written table fails a test instead of shipping.

## 2. Decisions (locked during brainstorm)

| ID | Decision | Rationale |
|----|----------|-----------|
| D1 | **A new xUnit collection `AppRoleTests` inside the existing `ProjectCeres.Tests` project**, in folder `Integration/AppRole/`, namespace `ProjectCeres.Tests.Integration.AppRole` — NOT a new `.csproj`. | All reuse infrastructure (`DualContextWebApplicationFactory`, `RlsTestFixture`, `FakeCurrentUserAccessor`, auth helpers) lives in `ProjectCeres.Tests`; a separate project couldn't see it without moving infra or `InternalsVisibleTo`. The one separate test csproj (`ProjectCeres.Analyzers.Tests`) is separate because it shares zero code — the opposite of this case. The roadmap's "test project" is shorthand; the namespace honors the intent. Sibling-collection precedent: `RlsTests`, `RateLimitTests`, `MfaRateLimitTests`. |
| D2 | **Curated write-path set (~10–15 tests), not a copy of the 60 auth classes, not a parameterized re-run.** | Copying is two diverging codebases AND mechanically impossible (the copied `DisposeAsync` cross-user cleanup hits `42501` under `ceres_app`). The value is in flows that *write user-owned rows through the wall*, where a missing policy is observable. |
| D3 | **Standalone, complementary tests — no forced sharing with existing admin-context auth tests.** | The AppRole tests assert a *different* property ("the wall holds for this write" + cross-user isolation) than the existing behavior tests. They are not duplicates, so there is nothing to keep in sync. |
| D4 | **Cleanup discipline: test bodies write/assert under `ceres_app`; seed + cleanup ALWAYS via `NewAdminContext()` (BYPASSRLS).** | The existing auth tests' `IgnoreQueryFilters().ExecuteDeleteAsync()` cross-user cleanup breaks under `ceres_app` (RLS blocks the cross-user delete; `IgnoreQueryFilters` only strips the EF filter, not the DB policy). Admin-context cleanup is the proven pattern (`RlsTestFixture.DisposeAsync`). |
| D5 | **D1-condition guard: the collection fixture's `InitializeAsync` asserts `SELECT rolbypassrls FROM pg_roles WHERE rolname='ceres_app'` is false; throws and refuses the whole suite if not.** | Fail-closed. A suite that goes green against a role secretly holding BYPASSRLS proves nothing. Mirrors `RlsParityStartupCheck`'s posture. One check guards every test; can't be forgotten per-test. |
| D6 | **No Stop-hook machinery change; the AppRole collection rides the existing Tier 2 (full-suite) routing.** | The new tests are `.cs` files under `ProjectCeres.Tests/`, which the existing tier logic already routes to the full suite. The roadmap's "tier adjusted" is satisfied by inclusion; documented in `testing.md`. |
| D7 | **Do NOT flip the `WafCollection` `UseAppRoleConnection` default for the existing 60 tests.** | That is a much larger migration (every `DisposeAsync` would need rewriting). 9.5d *adds* the app-role path alongside the admin default. Deferred. |

## 3. Architecture

```
ProjectCeres.Tests/                          (one .csproj — unchanged)
  Integration/
    Authentication/    (60 admin-context classes — UNCHANGED, ceres_admin)
    Rls/               (existing RLS-wall tests — UNCHANGED)
    DualContextWebApplicationFactory.cs       (9.5b — REUSED)
    AppRole/                                   (NEW)
      AppRoleCollection.cs                     [CollectionDefinition("AppRoleTests")]
                                               + the D5 pg_roles fail-fast guard
      Register…/Login…/MfaEnroll…/…Tests.cs    (the curated flows)
```

- **Collection fixture (`AppRoleCollection` + `ICollectionFixture<DualContextWebApplicationFactory>`):** the collection's `[CollectionDefinition("AppRoleTests")]`. The D5 guard runs once before any test (in a fixture `InitializeAsync` — implemented either on `DualContextWebApplicationFactory` itself if it already implements `IAsyncLifetime`, or on a small dedicated fixture composed alongside it; the plan resolves which).
- **Each test class** carries `[Collection("AppRoleTests")]`, injects `DualContextWebApplicationFactory`.

## 4. The test shape (canonical pattern every AppRole test follows)

```
InitializeAsync / arrange:   seed prerequisite rows via factory.NewAdminContext()  (BYPASSRLS)
act:                          drive the flow over HTTP via factory.CreateClient()
                              -> the request pipeline writes as ceres_app (RLS active)
assert (positive control):   factory.NewAppContext(actingAs: owner)
                              -> the written row IS visible to its owner
assert (negative control):   factory.NewAppContext(actingAs: otherUser)
                              -> the row is NOT visible to a different user
DisposeAsync / cleanup:       factory.NewAdminContext() + IgnoreQueryFilters().ExecuteDeleteAsync()
                              (admin role — the cross-user delete succeeds)
```

The positive control matters: without it, "row not visible to otherUser" could mean "RLS works" OR "the write silently failed / the app sees nothing." Both controls = a real assertion. (Same discipline as 9.5b's `RlsParityMetaTests` isolation test.)

## 5. The curated flow list (D2)

Each writes ≥1 user-owned table that the RLS wall protects. One small class per flow:

| Flow | User-owned table(s) written | Notes |
|------|------------------------------|-------|
| Register | `Settings`, `Categories`, (user row) | full new-user write fan-out |
| Login | `UserSession` | session row created under the role |
| Logout | `UserSession` (revoke) | |
| MFA enroll | `UserMfaBackupCode` | |
| MFA backup-code consume | `UserMfaBackupCode`, `TotpReplayEntry` | |
| Password-reset confirm | `PasswordResetToken`, `UserSession` (revoke) | pre-auth write path |
| Email-change confirm/revoke | `EmailChangeToken`, `UserSession` | |
| Email-confirmation | `EmailConfirmationToken` | **the table that shipped the original gap** |
| Lockout-unlock | `LockoutUnlockToken` | |
| Audit-log write | `AuditLog` | a representative authenticated action that records audit |

**Explicitly NOT re-run** (orthogonal to RLS — re-running buys no RLS signal, doubles surface): constant-time / timing-parity tests, rate-limit bucket tests, CSRF-token-shape tests, response-envelope-shape tests.

**Cross-tenant table note (for the plan author):** `FailedLoginAttempts` is cross-tenant by design (no RLS UserId policy, ADR-0067). If any curated flow touches failed-login recording, its cleanup of that table works under admin context for the reason "no policy to satisfy," not "BYPASSRLS needed" — not an inconsistency.

## 6. Pre-auth write subtlety (for the plan author)

Several curated flows (password-reset confirm, email-change confirm, email-confirmation, lockout-unlock) are **pre-auth** write paths: the request has no authenticated principal yet, so the GUC is set via `PreAuthUserScope` once the userId is known, not via the cookie. Under `ceres_app` these must already wrap their writes in `BeginPreAuthUserScopeAsync` (Stage 7.5 / the `[PreAuthScope]` services). The AppRole tests exercising these flows are the first tests that actually run them under RLS — if any pre-auth write is missing its scope, it surfaces here as `42501`. This is a feature (it's the class of bug 9.5d exists to catch), but the plan must expect it and treat a `42501` as "the production code is missing a `PreAuthUserScope`," fixed in production, not worked around in the test.

## 7. Docs to sync (at stage close)

- `docs/testing.md`: new "AppRole suite (Stage 9.5d)" subsection documenting the pattern (D4 cleanup discipline, D5 guard, the curated set). **Correct the stale line ~281** ("Do not create a second collection") — already untrue (`RlsTests`/`RateLimitTests`/`MfaRateLimitTests` are sibling collections; the latter two carry `DisableParallelization = true`); the real safety net is marker-scoped queries (`feedback_filter_test_queries_by_test_data`), not single-collection serialization. Note the AppRole collection rides Tier 2 (D6).
- `docs/roadmap-phase-three.md`: tick 9.5d at close-out.
- `docs/security-model.md`: note that auth write-flows are now RLS-verified under `ceres_app` (closes the test-blindness that let the 9.3 gap ship).

## 8. Out of scope (YAGNI)

- Flipping the `WafCollection` default to `ceres_app` for the existing 60 tests (D7 — deferred; much larger migration).
- A separate `.csproj` (D1).
- Re-running RLS-orthogonal auth tests (D2).
- Parameterizing the existing classes to run twice (D3 — their cleanup can't survive the role swap).

## 9. Success criteria

- The `AppRoleTests` collection exists; ~10–15 curated tests pass under `ceres_app`.
- The D5 guard throws (suite refuses) if `ceres_app` is given BYPASSRLS — verifiable by a meta-assertion or a documented manual check.
- Each test has both a positive (owner sees row) and negative (other user doesn't) control.
- `dotnet build` 0 errors / 0 CER errors; full suite green (existing 60 admin tests unaffected — D7).
- Docs synced (§7); roadmap 9.5d ticked.
```
