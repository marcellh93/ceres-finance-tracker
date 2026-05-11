# Stage 6.12 — Email-address-change flow — implementation log

**Date:** 2026-05-10
**Stage:** Phase 3, Stage 6c (Identity infrastructure, Batch 3b continued)
**Sub-stage:** 6.12 (email-address-change)
**Spec:** `docs/superpowers/specs/2026-05-10-stage-6-12-email-change-design.md`
**Status:** ✅ shipped

## What landed

Three endpoints (`POST /api/auth/email-change/request|confirm|revoke`), backed by:

- `ProjectCeres/Models/EmailChangeToken.cs` + `EmailChangeTokenPurpose` enum (`VerifyNew=1`, `RevokeOld=2`).
- `ProjectCeres/Migrations/20260510204310_AddEmailChangeTokens.cs` — single table, indexes `(UserId, ConsumedAt)` + `ExpiresAt` (parity with `PasswordResetTokens`).
- `ProjectCeres/Common/Authentication/EmailChangeTokenGenerator.cs` — 256-bit base64url, Argon2id-hashed (mirror of `PasswordResetTokenGenerator`).
- `ProjectCeres/Common/Authentication/EmailChangeService.cs` — `RequestAsync` / `ConfirmAsync` / `RevokeAsync` orchestration with per-user `SemaphoreSlim` and per-new-email `MemoryCache` rate gate (5/hour).
- `ProjectCeres/Common/Authentication/EmailChangeOutcomes.cs` — discriminated outcomes for the three operations.
- `ProjectCeres/Controllers/Api/EmailChangeController.cs` — `RequestChange` (`[RequireRecentAuth]`), `ConfirmChange` and `RevokeChange` (`[AllowAnonymous]` + `[EnableRateLimiting(AuthLoginByIp)]`).
- `ProjectCeres/ViewModels/Auth/EmailChange{Request,ConfirmRequest,RevokeRequest}.cs`.
- DI registrations in `Program.cs` (next to the password-reset registrations).
- Cross-feature edit in `ProjectCeres/Common/Authentication/PasswordResetService.cs` — `ConfirmAsync` now cancels any pending email-change for the same user and emails the old address.
- Test-only `ProjectCeres.Tests/Integration/CapturingEmailService.cs`.

## Tests

37 integration tests in `ProjectCeres.Tests/Integration/Authentication/EmailChange*`:

- `EmailChangeRequestTests.cs` (6) — happy path with two persisted rows + two captured emails with distinct tokens; reauth-required (negative: 0 rows, 0 emails); `EMAIL_ALREADY_IN_USE` (negative: 0 rows); `EMAIL_UNCHANGED` (negative: 0 rows); supersession; reauth-bypass negative.
- `EmailChangeConfirmTests.cs` (9) — Email + NormalizedEmail + UserName + NormalizedUserName + EmailConfirmed; sessions revoked + SecurityStamp regen; sibling RevokeOld consumed; invalid/expired/consumed tokens; collision-at-confirm with negative-assertion (token NOT consumed); negative-assertion lockout unchanged; new-and-old-address notification.
- `EmailChangeRevokeTests.cs` (7) — **critical** assertion `user.Email` unchanged; sibling VerifyNew consumed; negative-assertion sessions + SecurityStamp untouched; invalid/expired/consumed tokens; old-address-only notification.
- `EmailChangeConcurrencyTests.cs` (3) — two concurrent confirms (1 winner); two concurrent requests (one pair active); concurrent request + confirm (no row corruption).
- `EmailChangeCrossFeatureTests.cs` (4) — pending change + password-reset → both rows consumed; cancellation email to old address; subsequent confirm/revoke return 401.
- `EmailChangeRateLimitTests.cs` (4) — 11th confirm/revoke from same IP → 429; 6th request against same `newEmail` → 429 with `Retry-After`; bucket keyed by `newEmail` not `oldEmail`.
- 4 architecture additions in `ArchitectureTests.cs` — attribute matrix; CancellationToken last-param; controller does not field `Argon2idPasswordHasher` / `EmailChangeTokenGenerator`; no class-level `[AllowAnonymous]`.

## Decisions resolved during implementation

1. **Custom 256-bit + Argon2id tokens** (not `UserManager.GenerateChangeEmailTokenAsync`) — Identity's built-in has one global `TokenLifespan`, no built-in single-use tracking, no two-token-one-event semantics. Rolling our own mirrors the Stage 6c.1 password-reset choice.
2. **Single table with `Purpose` discriminator** — chosen over two tables for index parity with `PasswordResetTokens` and one-statement sibling consume on confirm/revoke.
3. **`/confirm` and `/revoke` are anonymous** — the email-link token IS the auth. Per-IP `AuthLoginByIp` (10/min) provides the only rate ceiling. AllowAnonymous lives on the actions, not the controller class.
4. **`/confirm` revokes all sessions + regens `SecurityStamp`; `/revoke` does NEITHER** — revoke is a cancel-pending-change, not a security event for the legitimate user.
5. **`/confirm` does NOT clear lockout** — explicit divergence from password-reset (email proof is not equivalent to password recovery). Negative-assertion test pins this.
6. **`SetEmailAsync` AND `SetUserNameAsync` on confirm** — registration sets `UserName == Email`; only updating `Email` would leave `FindByNameAsync(oldEmail)` resolving the user.
7. **Old-address notification on confirm** — discovered by re-reading security-model.md after the first happy-path test landed; the spec text "Notify the old address on successful completion of the change" had been overlooked. Implementation now sends to BOTH new and old addresses; test updated to `Confirm_sends_change_confirmed_email_to_BOTH_new_and_old_addresses`.
8. **Cross-feature: password-reset cancels pending email-change** — atomic with the password write inside the existing per-user semaphore in `PasswordResetService.ConfirmAsync`. Closes the window where an attacker-initiated email change with a still-live verify token could survive a victim's password-reset.
9. **Per-new-email rate-limit key** — service-side `MemoryCache` window keyed by lowercase-normalized **new** email, not old. The abuse vector is spamming a known address with verification mail.

## Architecture cleanup applied along the way

`Every_controller_action_declares_authorization_intent` (existing arch test) trips on `AmbiguousMatchException` when an action carries both `[Authorize]` and `[RequireRecentAuth]` because `RequireRecentAuth` inherits `AuthorizeAttribute` and the test calls the singular `GetCustomAttribute<AuthorizeAttribute>()`. Resolution: drop the redundant explicit `[Authorize]` on `RequestChange` — `[RequireRecentAuth]` already enforces `RequireAuthenticatedUser` plus the freshness gate. The arch test stays unchanged. The `EmailChangeController_has_correct_attribute_matrix` test asserts `[RequireRecentAuth]` directly.

## Two real bugs in the codebase, fixed while writing tests

None this stage. The implementation landed clean (the closest the suite came to a hidden bug was the spec-gap at item #7 — caught by re-reading the spec, not by a failing test).

## Documentation updates landed in this stage

- `docs/security-model.md` § Email Address Change — status banner + concurrency / rate-limit / cross-feature / lockout-divergence bullets.
- `docs/api-contract.md` — three new error-code rows; three new endpoint rows; password-reset endpoint row updated to mention cross-feature.
- `docs/roadmap-phase-three.md` — Stage 6.12 banner blockquote; five email-change checklist items flipped to `[x]`.
- `docs/planning-phase3.md` — Stage 6.12 entry next to the password-reset entry.
- `docs/planning-resolved.md` — Stage 6.12 entry with the nine policy decisions.
- `CHANGELOG.md` — `[Unreleased]` entry under "Authentication (Stage 6.12 — Email-address-change flow, 2026-05-10)".

## Outstanding Stage 6 work

Per `roadmap-phase-three.md` § Stage 6 close-out:
- 6.14 audit log table (`AuditLog` entity + writer service).
- Lockout self-service unlock signed-token endpoint.
- `AuthMfaByUser` rate-limit-partition follow-up (tracked in 6c.2 deferred decisions in `planning-phase3.md`).
- Stage 6 close-out documentation: visual auth-flow diagrams in `security-model.md` (deferred to a single end-of-stage pass).
