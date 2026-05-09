# ADR 0001: Authentication Deferred to Phase 3

## Status: Accepted (the inline "MFA via TOTP... is mandatory" clause in § Decision is superseded by [ADR-0069](ADR-0069-mfa-opt-in-for-personal-users.md), which moves MFA to opt-in. The surrounding decision — defer authentication to Phase 3 — remains accepted unchanged.)

## Context
During planning, the question arose of whether to build user authentication from the start.
The app is initially intended as a local personal tool for a single user on their own machine.
Authentication adds significant development complexity — login flows, password hashing, session
management, and eventually multi-tenancy — none of which provide any value when there is only
one user running the app locally with no network exposure.

The concern was whether skipping auth early would create painful retrofit work later.

## Decision
Authentication is not built in Phase 1 or Phase 2. The app runs as a single-user local tool
with no login. Authentication becomes a hard requirement in Phase 3, before any hosting or
external access is introduced. MFA via TOTP authenticator app (no SMS) is mandatory alongside
password-based login when it is built. Passwords must be hashed with Argon2.

## Consequences

**Positive:**
- Phase 1 scope stays tight — no auth complexity slowing down the core finance features
- Faster path to a working, usable app
- Auth is built once, correctly, when it is actually needed (Phase 3), rather than as a
  speculative early implementation that may need to be redesigned anyway

**Negative:**
- Phase 3 requires retrofitting multi-tenancy — all data queries will need to be scoped to
  the logged-in user, which touches every controller and repository
- The Settings table (single-row in Phase 1) must be migrated to a per-user preferences
  table in Phase 3, requiring a database migration
- There is a risk of shipping Phase 2 features that are harder to make multi-tenant later
  if assumptions about single-user data access are baked into the query logic
