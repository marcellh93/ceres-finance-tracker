# ADR-0064 — Social Login Deferred to Phase 4

**Status:** Accepted (Phase 3, Batch 3 — Auth)

**Date:** 2026-05-07

**Context:**

Phase 3 § Planned Features describes social login (Google, Facebook, Apple, Microsoft) as an **optional** alternative credential method, offered alongside email + password rather than as a replacement. Phase 3 launches as an invite-only beta with a solo developer and no support team.

The decision is whether to ship social login as part of the Phase 3 auth surface or defer it to a later phase.

The trade-offs:

**For shipping in Phase 3:**

- Lower signup friction
- Reduces support volume from forgotten passwords
- Stronger account security for users who run well-secured external identity accounts
- No password storage liability for those users

**Against shipping in Phase 3:**

- Each provider is its own integration with ongoing maintenance burden (API changes, edge cases, terms changes, key rotation)
- Apple Sign-In requires a paid Apple Developer account (€99/yr) and triggers App Store rules that mandate it if any other social option is offered
- Account linking and email-conflict resolution are non-trivial and have to be designed correctly the first time
- An external availability dependency: if the provider is down, those users cannot log in
- Privacy expectations of personal-finance users frequently include "I don't want Google to know which apps I use"
- Mandatory TOTP applies regardless of login method, which dilutes the friction-reduction argument
- Phase 3 already includes auth, multi-tenancy, GDPR baseline, hosting, and Razor → SPA cleanup. Adding four OAuth integrations on top is meaningful scope expansion.

**Decision:**

Phase 3 ships **email + password + TOTP only**. Social login is deferred to Phase 4 or later, decided after beta launch based on real signal.

The architecture is left open to social login without building anything behind the door:

- ADR-0063 chose `SameSite=Lax`, which permits OAuth top-level-navigation callbacks
- The `UserSession` table schema is provider-agnostic — it does not assume password authentication
- The user record's email column does not preclude later reconciliation with a provider-supplied email
- No `ExternalLogin` table is created; no OAuth provider configuration is added; no "Sign in with Google" buttons appear in the auth UI

**Rationale:**

The friction-reduction case for social login is real but partially neutralized by mandatory TOTP, which is the slow step in onboarding. The maintenance case against social login is substantial and ongoing, and a solo developer in beta does not have the operational headroom to absorb four parallel provider integrations on top of the rest of Phase 3.

Deferring lets the decision be made after beta launch with actual user signal — at that point, the right move may be to add one provider (probably Google for the autónomo audience) rather than four. The architectural cost of keeping the door open is essentially zero.

**Consequences:**

- Phase 3 auth UI contains only email + password forms; the auth-screen design (`planning-phase3.md` § 10) reflects this.
- The user model carries a `password_hash` column (Argon2id, ADR-pending parameters m=19456, t=2, p=1). This is not throwaway — email + password is required permanently regardless of whether social login is added later.
- When Phase 4 adds social login, the work required will be: add `ExternalLogin` table, add OAuth provider configuration (per chosen providers), add account-linking logic for cases where a user signs up via provider with an email already in `users`, update the auth UI. None of these blocked at the Phase 3 architecture level.
- Beta users tolerate the email + password + TOTP flow; this is a reasonable assumption for invite-only beta but should be re-evaluated before any open registration window.

**Cross-references:**

- ADR-0001 — Authentication deferred to Phase 3 (sets the broader phase context)
- ADR-0063 — `SameSite=Lax` (preserves social-login compatibility)
- `planning-phase3.md` § Planned Features — defines social login as optional
- `planning-phase3.md` § 10 Auth screen design — UI scope for Phase 3
