# ADR-0083 — Inactivity anonymisation reuses erasure's lanes via a shared service, and sets no re-registration hold

> **Diataxis type:** Explanation — architectural decision record.

**Status:** Accepted — 2026-10-10 (Stage 13.b).

**Phase:** Phase 3 (Hosted Beta).

**Supersedes:** None.

**Related decisions:** ADR-0029 (account-closure archive lifecycle — a different trigger, deferred); the Stage 13.9 right-to-erasure build (`ErasureExecutor`); Stage 13.a (Scrutor DI scan). Policy source: `security-model.md` § Inactive account policy; Decision C (`planning-resolved.md` § "Stage 13.b trigger/mechanism").

## Context

`security-model.md` sets a storage-limitation duty: 12 months with no login → warning email; 18 months with no login → anonymise the user-controlled profile and delete non-statutory data, with statutory financial records anonymise-retained. Stage 13.b builds this as the `InactivitySweep` tool.

Two design questions had no pre-existing answer and were resolved in brainstorming (user-ratified, 2026-10-10):

1. **How does the sweep get the "anonymise financial rows, keep them, redact support, delete throwaway, scrub identity" behaviour, which already exists inside `ErasureExecutor`?**
2. **What happens to the account identity after an inactivity anonymisation — does it mirror the erasure flow's 30-day re-registration hold?**

Facts that drove the decision (verified against the codebase during the Stage 13.b brainstorm):

- `ErasureExecutor`'s anonymise lanes (`AnonymiseStatutoryAsync`, `RedactSupportAsync`, `PurgeAsync`, `DeleteExportJobAsync`, `AnonymiseAccountAsync`) take only a `userId` and read nothing erasure-specific. Only the top (reading a `Sealed` `ErasureRequest`) and the tail (`GdprErasureCompleted` audit, `ErasedEmailHold`, request-status flip) are erasure-specific.
- An inactivity sweep is a **standing controller duty under GDPR Art. 5(1)(e)**, legally distinct from a user exercising the Art. 17 right to erasure. The `GdprErasureCompleted` audit row is the Art. 17 record; writing it for an inactivity cleanup would misrepresent a housekeeping action as a user-requested erasure.
- The 30-day `ErasedEmailHold` exists to prevent a just-erased user's email from being re-registered during a cooling-off window — a protection meaningful for a user who *asked* to vanish, not for an account the system cleaned up for dormancy.

## Decision

**1. Extract the shared anonymise lanes into a `UserAnonymiser` service with two callers.** The five lane methods move verbatim out of `ErasureExecutor` into `UserAnonymiser.AnonymiseAsync(userId, token, ct)`, which opens no transaction and writes no audit row — it runs inside the caller's ambient transaction. `ErasureExecutor` keeps its own tail (reads the request, calls `RecordEmailHoldAsync` **before** the anonymiser so the real email is still readable, then writes `GdprErasureCompleted` + flips status). `InactivitySweep` supplies its own tail (writes `InactivityAnonymised`). Both share one tested implementation of the statutory carve-out.

Rejected alternative: a second entry method on `ErasureExecutor`. It keeps a smaller diff but bolts a second, differently-behaving entrance onto a class whose name says "erasure" — the inactivity path would live inside an erasure object, misleading the next reader. The extraction gives the shared steps an honest home.

**2. Inactivity anonymisation sets `ErasedAt` but writes no `GdprErasureCompleted` and creates no `ErasedEmailHold`.** `ErasedAt` is set because an anonymised dormant account must still be refused login (the existing seal check keys on `SealedAt`/`ErasedAt`). But it is **not** an Art. 17 erasure: no `GdprErasureCompleted` row, and the email is freed immediately (no 30-day hold). The distinct `InactivityAnonymised` audit action records the event. If a former user returns and signs up again with the same email, nothing blocks them.

## Consequences

- One tested anonymisation implementation serves both flows; a fix to the statutory carve-out benefits both. The erasure flow's existing tests pin that its tail is unchanged after the extraction (regression guard).
- The legal distinction is enforced mechanically: a test asserts the inactivity path writes exactly one `InactivityAnonymised` row, zero `GdprErasureCompleted`, and no `ErasedEmailHold`.
- `InactivitySweep` and `UserAnonymiser` are both cross-tenant (`AdminDbContext`, BYPASSRLS) and are allow-listed in the `IgnoreQueryFilters` architecture test.
- The sweep skips any `SealedAt`/`ErasedAt` account, so it never collides with an in-flight Stage 13.9 erasure.
- Cron *registration* of `--run-inactivity-sweep` follows the Stage-16 deferred-cron pattern; the flag ships now.
- Account-*closure* archive (ADR-0029 `CustomerArchive`) remains deferred — a different trigger, no closure feature exists in code yet.
