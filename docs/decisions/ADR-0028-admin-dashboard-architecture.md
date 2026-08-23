# ADR-0028 — Admin Dashboard: ASP.NET Core Area with Immutable Audit Log (Phase 3)

**Status:** Accepted — **gating mechanism superseded by [ADR-0080](ADR-0080-admin-gating-via-live-role-policy.md)**

> **Superseded in part (2026-08-23).** The Area-plus-`[Authorize(Roles = "Admin")]` gating described below is obsolete: no Razor views remain, so there is no area to group, and role claims baked into the auth cookie are never re-issued mid-session — a revoked admin would keep access until the cookie expired. Admin surfaces are now `api/admin/*` controllers gated by a class-level `[RequireAdmin]` policy that checks the role live. See ADR-0080. **The audit-log requirement, the list of admin actions, and the isolation principle below all stand unchanged.**

**Context:**

When the app becomes multi-tenant in Phase 3, there is a need for an admin interface to manage users, monitor the system, handle compliance requests, and perform support actions. This interface must be strictly isolated from the user-facing app and every mutation it performs must be traceable.

Three structural options were considered:

1. **Admin routes mixed into the main app** — same controllers, guarded by role checks per action. Simple, but no clear boundary and easy to misconfigure.
2. **Separate admin application** — a second ASP.NET Core project. Strong isolation, but doubles the deployment surface and shares no code naturally.
3. **ASP.NET Core Area** — a first-class .NET feature that groups controllers, views, and layouts under a named area (`Admin`). Deployed as part of the same app, but physically separated in the project structure.

## Decision

The admin interface is implemented as an **ASP.NET Core Area** named `Admin`. All admin routes live under `/admin/*`. A separate Razor layout (`_Layout.cshtml` inside the area) is used — the admin UI is visually distinct from the user-facing UI.

The entire area is gated with `[Authorize(Roles = "Admin")]` applied at the area convention level, not per-controller.

### Admin Actions

The following actions are in scope for the admin dashboard:

**User/Tenant Management**
- View all users — list, search, filter by status and plan tier
- Activate / deactivate accounts (`IsActive = false` — consistent with ADR-0023; never hard delete)
- Force password reset or revoke all active sessions for a user
- Impersonate a user for support purposes (see impersonation section below)

**System-Defined Lookup Tables**
- Manage `AccountType`, `CategoryType`, `ReportType`, `Currency` — these are the only entities that are admin-owned rather than user-owned. All other entities belong to a tenant.

**Compliance**
- Trigger a GDPR data export for a specific user (Right of Access / Right to Portability)
- Process a GDPR erasure request — the one context where cascading deletes are permitted, executed under admin authority with a mandatory confirmation step and audit record
- View the audit log for a specific user

**System Health**
- View aggregate stats: total users, active users, per-tenant filesystem storage usage
- View system-wide audit log

### Impersonation

Admin impersonation uses an explicit session model:

- Admin clicks "Impersonate" on a user — a new impersonation session is created, logged with a unique `ImpersonationSessionId`
- Every action taken during impersonation is tagged with that session ID in the audit log
- The admin ends impersonation explicitly — the session is closed and logged
- Impersonation is not available for other admin accounts

### Audit Log

Every admin mutation writes an append-only record. The table is never exposed through an edit or delete endpoint — not even to admins.

```
AdminAuditLog
  Id                     (UUID)
  AdminUserId            (FK — who performed the action)
  TargetUserId           (UUID, nullable — null for system-level actions)
  Action                 (enum: Deactivate, Reactivate, ForceLogout, Impersonate,
                                ImpersonateEnd, GdprExport, GdprErasure,
                                PasswordReset, PlanOverride, ...)
  Detail                 (JSON — before/after state or relevant parameters)
  PerformedAt            (UTC timestamp)
  ImpersonationSessionId (UUID, nullable — links all actions within one impersonation session)
```

### What Admin Cannot Do

- Read a user's transaction data directly outside of an impersonation session — raw data access bypasses the audit model
- Hard delete accounts or categories outside of a GDPR erasure flow
- Edit or delete audit log records

## Consequences

**Positive:**
- ASP.NET Core Areas provide clean physical separation with no routing ambiguity
- A single `[Authorize(Roles = "Admin")]` area convention prevents misconfigured controller-level gaps
- The append-only audit log makes all admin actions traceable and non-repudiable
- Impersonation is audited end-to-end — support sessions cannot be hidden

**Negative:**
- The area adds structural complexity to the project before it is needed — this is a Phase 3 concern and must not be built in Phase 1 or 2
- Impersonation requires careful session handling to avoid leaking admin identity into user-facing views

**Related decisions:**
- ADR-0001: authentication is deferred to Phase 3 — admin roles depend on auth existing
- ADR-0023: soft delete (deactivation) applies to user accounts — admin follows the same rule
- ADR-0018: UUID primary keys apply to `AdminAuditLog.Id` and `ImpersonationSessionId`
- `docs/security-model.md`: admin access controls and IDOR prevention rules apply here too
