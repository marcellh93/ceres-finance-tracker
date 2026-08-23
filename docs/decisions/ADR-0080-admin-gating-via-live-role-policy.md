# ADR-0080 — Admin gating via a live role policy, not `[Authorize(Roles = "Admin")]` (Phase 3)

**Status:** Accepted

**Supersedes:** the gating mechanism of [ADR-0028](ADR-0028-admin-dashboard-architecture.md). ADR-0028's audit-log decision, its list of admin actions, and its isolation principle all stand unchanged.

**Context:**

ADR-0028 decided that the admin interface would be an ASP.NET Core Area at `/admin/*`, gated at the area convention level with `[Authorize(Roles = "Admin")]`. Two things have changed since it was written.

**The Razor layer is gone.** ADR-0028 assumes server-rendered views and an area layout (`_Layout.cshtml`). No Razor views remain; the client is a React SPA and the server exposes JSON APIs. An MVC Area has nothing to group — there are no views or layouts to separate, only controllers.

**The attribute does not work in this codebase.** Role claims are written into the auth cookie when the user signs in. This project's `OnValidatePrincipal` hook, `SessionRevocationValidator`, only ever calls `RejectPrincipal()` — it never calls `ReplacePrincipal`, so a principal is never rebuilt mid-session. Two consequences follow, and both are wrong:

- A role **granted** after sign-in stays invisible until the user logs out and back in. Every admin created through the promote endpoint would be refused by a claims-based check for the rest of their session — the feature would appear broken to the person who just used it.
- A role **revoked** after sign-in is still honoured until the cookie expires: up to 30 minutes on a sliding session, or 30 days on a persistent one. That is a privilege-retention window on the most sensitive role in the system.

The second point is the serious one. Under ADR-0028's mechanism, removing someone's admin access does not actually remove it.

## Decision

Admin surfaces are gated by **`[RequireAdmin]` applied at the class level**, backed by the `AdminLive` authorization policy. The policy's handler resolves the caller from `ClaimTypes.NameIdentifier` and asks the database whether they hold the role **on every request**, via `AdminRoleService.IsAdminAsync`.

`[Authorize(Roles = "Admin")]` is not used anywhere in this codebase. `AppRoles.cs` carries a remark saying so, because that file is where someone reaching for role-based authorization will look first.

Three properties this buys:

1. **Grants and revocations take effect on the next request**, with no re-login and no cookie reissue.
2. **The gate is declarative and inherited.** A per-action check would be satisfiable by omission — a new action that simply forgot the check would be admin-only in name and merely authenticated-only in fact, and neither the compiler, the architecture tests, nor code review would catch the absence of two lines. Class-level placement covers every present and future action.
3. **It mirrors an existing project pattern.** `RequireRecentAuthAttribute` + the `RecentAuth` policy already establish the attribute-subclassing-`AuthorizeAttribute` shape; `RequireAdmin` follows it rather than inventing a second idiom.

### Routing

Admin endpoints live under `api/admin/*` as ordinary API controllers, not in an MVC Area. The `Admin` **namespace** (`ProjectCeres/Admin/`) remains the isolation boundary that ADR-0065 reserves for cross-user queries, enforced by an architecture test. Isolation is preserved; it is expressed as a namespace and a policy rather than as an area convention.

### Cost

Each admin request costs one extra database read for the role check. Admin endpoints are low-traffic by nature, and the alternative is a privilege-retention window measured in days.

## Consequences

**Positive.** Revocation is immediate. The gate cannot be silently skipped by a new action. The pattern matches the existing reauth precedent, so there is one idiom to learn rather than two.

**Negative.** A database round-trip per admin request. A future reader who knows ASP.NET Core will reach for `[Authorize(Roles = ...)]` by reflex and must be told not to — hence the remark in `AppRoles.cs` and rule 2 in `ProjectCeres/Admin/README.md`.

**Deferred.** Refreshing role claims mid-session — via `RefreshSignInAsync`, which the reauth flow at `security-model.md` § Reauthentication already uses and which does rebuild the principal — is scheduled for Stage 15.8. If that lands, `[Authorize(Roles = ...)]` becomes viable again, but the live check would still be the stronger option for revocation and this ADR would not automatically be reversed.

**Enforcement.** `AdminAuthorizationTests` pins both halves: that the gate sits on the class with no action opting out, and that a role granted after sign-in is honoured on the next request. Removing `[RequireAdmin]` fails three of that suite's nine tests.

## Related

- [ADR-0028](ADR-0028-admin-dashboard-architecture.md) — superseded on gating mechanism only; its audit-log and isolation decisions stand.
- [ADR-0065](ADR-0065-ef-global-query-filters-with-explicit-redundancy.md) — reserves `Admin/` as the only namespace permitted to bypass the per-user query filter.
- `docs/superpowers/specs/2026-08-22-shared-category-catalogue-design.md` — the spec Stage 15.6 implements.
