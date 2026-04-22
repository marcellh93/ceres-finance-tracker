# ADR 0053: Guided Onboarding Deferred to Phase 3

## Status: Accepted

## Context

A guided onboarding experience — walking a new user through entering what they own and
owe to produce an immediate net worth number — was listed as a Phase 2 feature. The
motivation was readiness for Phase 3: when the app opens to beta users, cold onboarding
is the first impression. YNAB's largest churn driver is complexity before value.

## Decision

**Deferred to Phase 3.**

Rationale:
- Phase 2 is single-user. Building a first-run experience for a user who already knows
  the app (the developer) has no validation value.
- Onboarding is the first impression for Phase 3 beta users — it must be built and
  validated in the context it will actually be used: multi-user, hosted, with real
  strangers experiencing the app for the first time.
- No hosting, no auth, and no multi-tenancy exist before Phase 3. Onboarding without
  these is not representative of the real experience.
- Phase 3 cannot begin until all necessary parts are in place — onboarding is one of
  those parts, not a prerequisite to it.

## Consequences

**Positive:**
- Onboarding is designed with real Phase 3 constraints in mind — auth flow, account
  creation in a multi-user context, and the actual first-time user experience
- No wasted effort building a flow for a single known user

**Negative:**
- Phase 2 has no guided setup experience — new test users trying the app before Phase 3
  must be walked through manually
