# Stage 13.9 — Right to Erasure (GDPR Art. 17) — Design

**Status:** Draft for review (2026-09-25)
**Stage:** 13.9 (Phase 3 — GDPR baseline)
**Depends on:** Stage 13.8 (reuses `UserContentEntities`, the `/settings/account` page, the `ExportJob` file-cleanup pattern, `TokenLookupHasher`, the `ExportJobWorker`/`SweepSessions` cron pattern) · Stage 6.15 (`TokenLookupHasher`) · Stage 7.5 (RLS + `AdminDbContext`).
**Relates to:** ADR-0029 (churn vs erasure lifecycle — see § Deviations).
**Forward dependency (documented):** the retained erasure-audit row is pseudonymised with a **local HMAC now**, migrated to the canonical `UserRef` when Stage 15 ships. 13.9 ships fully with the local HMAC; the Stage-15 swap is a mechanical re-key.

---

## 1. Purpose

Let a user erase their account and personal data (GDPR Art. 17), while (a) retaining statutory financial records in anonymised form (Art. 17(3)(b)), (b) retaining a de-identified support-knowledge record, and (c) keeping one pseudonymised proof-of-erasure audit row. Reuses the Stage 13.8 self-service surface and infrastructure.

## 2. Decisions locked in brainstorming (2026-09-25)

| # | Decision | Rationale |
|---|---|---|
| D1 | **Erasure-audit pseudonym = local HMAC now, migrate to `UserRef` at Stage 15.** | Unblocks the legal gate; reuses existing keyed-hash tech; the Stage-15 swap is a mechanical re-key. |
| D2 | **Support correspondence = B3a (redact-retain).** On erasure, scrub the erased user's own identifiers + generic email/IBAN patterns from message bodies, retain the redacted thread as a de-identified knowledge record; delete support-attachment files. | Keeps problem-solving history for analysis (the user's stated need) while genuinely erasing the person. Pseudonymisation-isn't-erasure: the body must be scrubbed, not just the identity FK. |
| D3 | **Email re-registration = after a 30-day cooling-off**, then re-registerable. | Guards against instant re-claim / accidental-erasure regret without a permanent tombstone. |
| D4 | **Timing = confirm → immediately sealed → 72h cancel-only hold → irreversible delete.** The seal is a pre-delete hold, NOT a restorable archive. Cancellable only via the emailed cancel link during the 72h. | The friction + hold guards against accident/coercion; the non-restorable seal keeps it distinct from the churn archive (ADR-0029 forbids mixing). **Billing-era refinement (future, planning-future.md):** the hold extends to the paid-through date once billing exists. |
| D5 | **Execution = external-cron worker** (`--run-erasure-jobs`), the SweepSessions/ExportJobWorker pattern via `AdminDbContext`. | The recorded scheduler decision; survives multi-instance; synchronous rejected. |
| D6 | **Three lanes:** purge (`UserContentEntities` minus the statutory set), anonymise-and-retain (`StatutoryRetentionSet` — financial records), redact-retain (support, via `IdentifierRedactor`). | Each user-content table has exactly one fate; making the sets explicit + tested stops an entity landing in the wrong lane. |
| D7 | **Files deleted explicitly** (not via DB cascade): support-attachment files + any outstanding `ExportJob` ZIP + row. | A DB cascade never runs app code → strands files on disk (models.md known gap). |

## 3. Deviations from existing docs (verify-against-codebase findings — READ)

- **`ClosureType` / `CustomerArchive` do NOT exist yet.** ADR-0029 *describes* a churn-vs-erasure lifecycle with a `ClosureType` enum and a `CustomerArchive` entity, but neither is built (churn is a later stage, tracked by ADR-0029 itself). 13.9 stands alone: erasure is recorded with a new **`ErasedAt` timestamp** (+ the seal state, §4) on `ApplicationUser` — the minimum to mark "erased" and block re-login. When churn/`CustomerArchive` is built, it adopts `ClosureType`; 13.9's `ErasedAt` is forward-compatible. The spec cross-references ADR-0029 as the eventual home, not a current dependency.
- **Erasure hard-deletes Accounts/Categories — a documented exception to CLAUDE.md's "deactivate, never hard delete."** That rule governs *normal* lifecycle (an active user archiving an account keeps transaction history). GDPR erasure is the one path where hard-delete is correct and legally required. This is intentional; an implementer/reviewer must NOT "fix" it back to deactivation. Softening: financial records subject to statutory retention are **anonymised-and-retained** (the statutory lane), not hard-deleted — so the collision is only with non-statutory user content.

## 4. Data model

**New entity `ErasureRequest` (user-owned, RLS `user_isolation`, 5-registry):**
```
ErasureRequest : IUserOwned
  Id                 Guid  PK
  UserId             Guid  NOT NULL (RLS discriminator)
  Status             enum ErasureStatus: Sealed=0, Cancelled=1, Completed=2
  RequestedAt        DateTime NOT NULL
  ExecuteAfter       DateTime NOT NULL  (= RequestedAt + 72h)
  CancelTokenLookup  byte[]  NOT NULL, filtered-unique  (HMAC — the cancel link)
  CancelTokenHash    varchar(512) NOT NULL              (Argon2id — 2nd factor, mirrors ExportJob)
  CompletedAt        DateTime?
  CancelledAt        DateTime?
```
The cancel link is two-factor exactly like the 13.8 download token (HMAC lookup + Argon2id verify; raw token only in the email).

**`ApplicationUser` new columns:** `SealedAt DateTime?` (set on request; the seal), `ErasedAt DateTime?` (set on completion). No `ClosureType` (see §3). Email freed for re-registration 30 days after `ErasedAt` (D3) — enforced at registration (a check, not a column).

## 5. Sealed-account enforcement

`SessionRevocationValidator.ValidateAsync` (the existing cookie-validation hook that already `RejectPrincipal()`s a revoked session) gains a sealed-account check: if the user's `SealedAt` is set, reject the principal (immediate logout, no data access) — the same mechanism revocation already uses. Login likewise refuses a sealed/erased account. So the moment a request is confirmed, the user is locked out; the 72h hold is server-side only (the cancel link is the sole re-entry, and it un-seals).

## 6. API surface (`ProfileApiController`, extending 13.8's)

- `POST /api/profile/erasure` — `[Authorize]` + `[RequireRecentAuth]`; body carries the typed-confirmation token (e.g. `{ "confirm": "ERASE" }`) validated → else `422`. Creates the `ErasureRequest` (Sealed), sets `ApplicationUser.SealedAt`, writes an `AuditLogAction.GdprErasureRequested` audit row, sends `GdprErasureInitiated` (with cancel link). `202 Accepted` `{ data: { message } }`. Idempotent: an existing Sealed request returns it. Rate-limited (reuse a `*ByUser` policy).
- `POST /api/profile/erasure/cancel` — body/query `token`; `[AllowAnonymous]` (the sealed user can't authenticate — the token IS the auth, like the email-change revoke). Two-factor verify → sets Status=Cancelled, clears `SealedAt`. `204`/`404`/`410` (already-executed/expired).

## 7. Components

| Unit | Responsibility | New/reuse |
|---|---|---|
| `ErasureRequest` + migration | Durable job (§4), 5-registry | New |
| `ApplicationUser.SealedAt/ErasedAt` + migration | Seal + erased markers | New columns |
| `ErasureService` | Create (seals + audit + email), cancel (token-gated, un-seals) | New |
| Seal check in `SessionRevocationValidator` | Reject a sealed principal | Extend existing |
| `ErasureExecutor` | The three-lane sequence (§8), under `AdminDbContext` | New |
| `IdentifierRedactor` | Pure `Redact(body, knownIdentifiers)` — two-pass (§9) | New |
| `StatutoryRetentionSet` | The anonymise-retain subset (financial records under Código de Comercio/LGT) | New named list |
| `UserContentEntities` | The full content set; purge lane = it minus statutory minus support | Reuse (13.8) |
| `ErasureWorker` (`--run-erasure-jobs`) | Cron: drain Sealed-past-ExecuteAfter, run executor, idempotent/retry/cleanup | New; `[RequiresAdminContext]`, SweepSessions pattern |
| Erasure pseudonym helper | Local HMAC(user id) for the retained audit row | New (reuse keyed-hash; → UserRef at 15) |
| `GdprErasureInitiated` email | Confirmation + cancel link (EN+ES) | New `EmailTemplateKey` |
| `/settings/account/erasure` page | Destructive-confirm UI (typed confirm, what's deleted/retained), `AlertDialog` + `useStepUp` | New — via frontend-orchestrator |

## 8. ErasureExecutor sequence (per eligible job, `AdminDbContext`, `IgnoreQueryFilters().Where(UserId==)`)

1. **Anonymise-and-retain** — for each `StatutoryRetentionSet` entity, replace identity fields with anonymous tokens; keep the rows for the statutory period.
2. **Redact-retain support (B3a)** — for each support message: `IdentifierRedactor.Redact(body, known)` → store the redacted body; strip the identity FK; delete the attachment **files** (via the file-deletion service). The redacted thread survives as de-identified knowledge.
3. **Purge** — hard-delete every remaining user-content entity (the purge set = `UserContentEntities` minus statutory minus support).
4. **Files** — delete any outstanding `ExportJob` ZIP + row for the user.
5. **Account** — set `ErasedAt`; keep `SealedAt`; anonymise the `ApplicationUser` identity fields (name/email → anonymous tokens) so the account row itself carries no PII; no `CustomerArchive` created. **Re-registration interaction:** the 30-day cooling-off (D3) blocks re-registering the *original* email — but that email is being anonymised here, so the cooling-off is tracked by a separate mechanism, not by reading the (now-anonymised) email column. Store the original email's HMAC fingerprint (via `TokenLookupHasher`) + `ErasedAt` in a small `ErasedEmailHold` record (or reuse the audit row's data); registration checks a new email's fingerprint against un-expired holds. This keeps the email genuinely erased from the account row while still enforcing the 30-day block. (The hold fingerprint is itself deleted after the 30 days.)
6. **Audit** — write one `GdprErasure`-completed audit row with the user id replaced by the local HMAC pseudonym.
Status → Completed.

## 9. `IdentifierRedactor` (B3a core)

`string Redact(string body, ErasureIdentifiers known)` — pure, no I/O.
- **Pass 1 (targeted):** replace the erased user's own known values (email, display name, on-file account identifiers) literally found in the body with `[redacted]`. High-precision; the load-bearing pass.
- **Pass 2 (generic):** conservative regex scrub of email-shaped and IBAN-shaped tokens — catches a second email / an IBAN the user typed that isn't their on-file value.
- **Explicit non-goal:** not a general PII/NER engine. A free-text home address or phone typed in prose is NOT caught — accepted, documented (the legal obligation is *this user's* identifiers, covered by pass 1). Pass 2 carries a small false-positive risk (an IBAN-shaped string that isn't one) — acceptable for a knowledge record (a stray `[redacted]` beats a leak); patterns kept conservative.

## 10. Error handling / idempotency (worker)

- Claim Sealed→(in-progress marker) before deleting; a re-run must not double-execute.
- The executor is **resumable/idempotent**: each lane checks-then-acts so a mid-run crash + re-run completes without error (e.g. deleting an already-deleted file is a no-op; anonymising an already-anonymised row is a no-op).
- On unrecoverable failure: leave the job non-Completed, log, alert — do NOT mark Completed. A partially-erased account stays sealed (safe: no data access) until the run completes.
- Cancellation races the executor: the executor re-checks Status==Sealed at claim time; a Cancelled job is skipped.

## 11. Testing (ship-gate — negative assertions)

- Reauth-gated request → without recent auth `401`; wrong typed-confirm `422`.
- Request seals the account: a sealed user's next authenticated request is rejected (logout); login refused.
- Cancel via valid token within 72h → un-sealed, Status=Cancelled; cancel after execute/expiry → `410`; foreign/invalid token → `404`.
- **Executor end-to-end:** User A erased → no A personal data remains in the purge-set tables; financial (statutory) rows remain but **anonymised** (identity tokens, not A's name/email — negative assertion); support threads remain but **redacted** (A's email/name absent from bodies — negative assertion), attachment files gone; ExportJob ZIP gone; exactly one erasure audit row remains, pseudonymised (not A's id).
- `IdentifierRedactor` unit table: A's email removed; a different email caught (pass 2); an IBAN caught; ordinary problem text untouched.
- Idempotency: a crash-then-rerun completes; a Cancelled job is skipped by the worker.
- Three-lane coverage: a test asserts every `UserContentEntities` entity is in exactly one of {purge, statutory, support} — so a new content table can't silently escape a lane.
- BDD (Reqnroll): request → cancel-link aborts (account restored); request → worker after 72h → erased (login refused, data gone).

## 12. Out of scope (deferred)
- `ClosureType` / `CustomerArchive` / churn lifecycle (a later stage; §3).
- Restoration (ADR-0029: none for erasure, ever).
- `UserRef` canonical pseudonym (Stage 15 — 13.9 uses a local HMAC).
- Billing-tied hold window (Phase 5; D4 — recorded in planning-future.md).
- Full free-text PII/NER redaction (§9 non-goal).

## 13. Docs to sync on completion
- `api-contract.md` (erasure + cancel endpoints), `models.md` (`ErasureRequest`, `ApplicationUser` columns), `security-model.md` (seal enforcement, the erasure data-handling model), `legal.md` (**needs the D4 non-restorable-hold clarification + the B3a support-retention basis — READ-ONLY, flag for user confirmation**), `roadmap-phase-three.md` §13.9 checklist, CHANGELOG.
