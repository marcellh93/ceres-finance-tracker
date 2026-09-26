# Stage 13.9 — Right to Erasure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Let a user irreversibly erase their account and personal data (GDPR Art. 17) — via a confirm→seal→72h-cancel-hold→delete flow — while anonymising-and-retaining statutory financial records, redact-retaining support knowledge, and keeping one pseudonymised proof-of-erasure audit row.

**Architecture:** A reauth-gated request seals the account immediately (enforced at the existing cookie-validation hook) and writes a durable `ErasureRequest` job; after a 72h cancel-only hold an external-cron worker (`--run-erasure-jobs`, the `SweepSessions`/`ExportJobWorker` pattern via `AdminDbContext`) runs a three-lane delete (purge / anonymise-retain / redact-retain), deletes files explicitly, marks the account `ErasedAt`, and writes a pseudonymised audit row.

**Tech Stack:** .NET 10 / ASP.NET Core Identity / EF Core + Npgsql + RLS; xUnit + Reqnroll; React 19 + Vite + shadcn/base-ui.

**Spec:** `docs/superpowers/specs/2026-09-25-stage-13-9-right-to-erasure-design.md`

## Global Constraints

- **Erasure HARD-DELETES Accounts/Categories — a DOCUMENTED exception to CLAUDE.md's "deactivate, never hard delete."** That rule governs normal lifecycle; GDPR erasure is the one legally-required hard-delete path. Do NOT "fix" it back to deactivation. Statutory financial records are anonymise-and-retained (not deleted) — the statutory lane.
- **`ClosureType` / `CustomerArchive` do NOT exist** (ADR-0029 describes them; churn is unbuilt). Use a new `ErasedAt` timestamp on `ApplicationUser`. Do NOT reference/require `ClosureType` or `CustomerArchive`.
- **Seal is enforced by EXTENDING `SessionRevocationValidator.ValidateAsync`** (the existing cookie hook, already on `AdminDbContext`) — a sealed user's principal is rejected + signed out, same mechanism as a revoked session. Do NOT build a new middleware.
- **Two-factor cancel link** = HMAC `CancelTokenLookup` (via `TokenLookupHasher`) + Argon2id `CancelTokenHash` (via a token generator mirroring `ExportTokenGenerator`); raw token only in the email.
- **Worker + service RLS discipline:** the worker/executor run cross-user via `AdminDbContext` + `IgnoreQueryFilters().Where(UserId == userId)` (allow-listed in `ArchitectureTests`, `[RequiresAdminContext]` on the worker); the request/cancel service is RLS-scoped (`.Owned(user)`) except cancel (token-gated, pre-auth — admin-context lookup like the other token confirms).
- **Reuse `UserContentEntities`** (the 13.8 seam) as the content set; the purge lane = it minus `StatutoryRetentionSet` minus the support tables.
- **Status codes:** 202 (request accepted), 422 (bad typed-confirm/format — `UnprocessableEntity`, never 400), 204/404/410 (cancel).
- **No `Co-Authored-By:` trailer.** Stay on `main`. Comments short.
- **`legal.md` is READ-ONLY** — the two additions it needs (D4 non-restorable-hold clarification; B3a support-retention legitimate-interest basis) are surfaced for USER SIGN-OFF in Task 11, not edited autonomously.
- **The SPA erasure page (Task 9) routes through `frontend-orchestrator`**, not a plain implementer.

---

### Task 1: `ErasureRequest` entity + `ApplicationUser` seal columns + migration (5-registry)

**Files:**
- Create: `ProjectCeres/Models/ErasureRequest.cs`
- Modify: `ProjectCeres/Models/ApplicationUser.cs` (add `SealedAt`, `ErasedAt`), `ProjectCeres/Data/AppDbContext.cs` (DbSet + config), `ProjectCeres/Common/UserOwnedModel.cs` (`ErasureRequests` → `AuthInternalTables`, like `ExportJobs`)
- Create migration: `AddErasureRequest`
- Test: `ProjectCeres.Tests/Integration/Rls/ErasureRequestRlsTests.cs`, `ProjectCeres.Tests/Unit/UserOwnedModelTests.cs` (count-pin bump)

**Interfaces:**
- Produces: `ErasureRequest : IUserOwned` (fields per spec §4); `ErasureStatus { Sealed=0, Cancelled=1, Completed=2 }`; `ApplicationUser.SealedAt`/`.ErasedAt` (`DateTime?`); `AppDbContext.ErasureRequests`.

- [ ] **Step 1: Write the entity** (mirror `ExportJob.cs` for the two-factor token fields + `AuthInternalTables` membership rationale):

```csharp
// ProjectCeres/Models/ErasureRequest.cs
using ProjectCeres.Common;
namespace ProjectCeres.Models;

public enum ErasureStatus { Sealed = 0, Cancelled = 1, Completed = 2 }

public sealed class ErasureRequest : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ErasureStatus Status { get; set; } = ErasureStatus.Sealed;
    public DateTime RequestedAt { get; set; }
    public DateTime ExecuteAfter { get; set; }   // RequestedAt + 72h
    public byte[] CancelTokenLookup { get; set; } = Array.Empty<byte>();
    public string CancelTokenHash { get; set; } = "";
    public DateTime? CancelledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

- [ ] **Step 2: Add `ApplicationUser` columns** — `public DateTime? SealedAt { get; set; }` and `public DateTime? ErasedAt { get; set; }`.
- [ ] **Step 3: Register DbSet + config** in `AppDbContext` beside `ExportJob` (Stage 13.8): `HasKey`, `Property(TokenHash-analog).HasMaxLength(512)`, filtered unique index on `CancelTokenLookup` (`HasFilter("octet_length(\"CancelTokenLookup\") > 0")`), `HasIndex(new { UserId, Status })`.
- [ ] **Step 4: Add `"ErasureRequests"` to `UserOwnedModel.AuthInternalTables`** (bookkeeping, excluded from `FinanceTables`/`UserContentEntities` — an erasure job is not itself user content to erase). Bump the `UserOwnedModelTests` count pin.
- [ ] **Step 5: Write the RLS isolation test** (mirror `ExportJobRlsTests` post-migration: seed User A's request via admin, query as User B → empty; plus the WITH-CHECK insert-rejection + positive self-visibility, the trio 13.8 landed).
- [ ] **Step 6: Create + apply migration** `dotnet ef migrations add AddErasureRequest`; the migration adds the `ErasureRequests` table (RLS `user_isolation` block, copy the `AddExportJobs` per-table pattern) AND the two `ApplicationUser` columns. `dotnet ef database update --connection <ceres_migrator>`.
- [ ] **Step 7:** Run the RLS + parity + drift + count-pin tests → green. **Commit.**

---

### Task 2: Seal enforcement in `SessionRevocationValidator`

**Files:**
- Modify: `ProjectCeres/Common/Authentication/SessionRevocationValidator.cs`
- Test: `ProjectCeres.Tests/Integration/Authentication/` (a sealed-account rejection test, mirror the revoked-session test)

**Interfaces:**
- Consumes: `ApplicationUser.SealedAt` (Task 1).

- [ ] **Step 1: Failing test** — an authenticated request from a user whose `SealedAt` is set is rejected (principal rejected + signed out), like a revoked session.
- [ ] **Step 2: Run — fails** (no seal check yet).
- [ ] **Step 3: Implement** — after the existing session lookup (line ~43-49), load the user via the same `AdminDbContext` and reject if sealed:

```csharp
// Sealed account (Stage 13.9): an erasure request seals the account immediately.
// A sealed principal is rejected + signed out on the next request — same mechanism
// as a revoked session. The 72h hold is server-side; the emailed cancel link un-seals.
var user = await db.Users.IgnoreQueryFilters()
    .Where(u => u.Id == session.UserId && u.SealedAt != null)
    .FirstOrDefaultAsync();
if (user is not null) { await RejectAsync(ctx); return; }
```

- [ ] **Step 4: Run — passes.** Also confirm login refuses a sealed/erased account (add/extend the login test).
- [ ] **Step 5: Commit.**

---

### Task 3: `IdentifierRedactor` (B3a core — pure function)

**Files:**
- Create: `ProjectCeres/Services/IdentifierRedactor.cs`
- Test: `ProjectCeres.Tests/Unit/IdentifierRedactorTests.cs`

**Interfaces:**
- Produces: `record ErasureIdentifiers(string Email, string DisplayName, IReadOnlyList<string> AccountTokens)`; `static string Redact(string body, ErasureIdentifiers known)`.

- [ ] **Step 1: Failing tests** (the table — every one a real assertion):

```csharp
[Fact] public void Redacts_the_users_own_email()      // body "reach me at a@b.com" + known.Email a@b.com → "[redacted]"
[Fact] public void Redacts_the_users_display_name()
[Fact] public void Pass2_catches_a_different_email()  // a SECOND email not in known → still [redacted] (generic pass)
[Fact] public void Pass2_catches_an_iban()            // "ES91 2100 0418 4502 0005 1332" → [redacted]
[Fact] public void Leaves_ordinary_problem_text_untouched() // "CSV import failed on semicolons" → unchanged
```

- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement** — pass 1: replace each non-empty `known` value (email, name, account tokens) literally (case-insensitive for email/name). Pass 2: regex-replace email-shaped (`\b[\w.+-]+@[\w-]+\.[\w.-]+\b`) and IBAN-shaped (`\b[A-Z]{2}\d{2}[A-Z0-9 ]{10,30}\b`) tokens with `[redacted]`. Conservative patterns; pure, no I/O.
- [ ] **Step 4: Run — passes. Commit.**

---

### Task 4: `StatutoryRetentionSet` + the three-lane split

**Files:**
- Create: `ProjectCeres/Common/StatutoryRetentionSet.cs`
- Test: `ProjectCeres.Tests/Unit/ErasureLaneCoverageTests.cs`

**Interfaces:**
- Produces: `StatutoryRetentionSet.Tables` (the financial-record tables anonymise-and-retained under Código de Comercio/LGT — transactions, transfers, liability_payments, accounts as they carry statutory records); a helper `ErasureLanes.Classify(model)` returning `(purge, statutory, support)` partitions of `UserContentEntities.List(model)`.

- [ ] **Step 1: Failing test (lane coverage — the guard):**

```csharp
[Fact] public void Every_user_content_entity_is_in_exactly_one_lane()
{
    var lanes = ErasureLanes.Classify(model);
    var all = UserContentEntities.List(model).Select(t => t.PostgresTableName).ToHashSet();
    var covered = lanes.Purge.Concat(lanes.Statutory).Concat(lanes.Support).ToList();
    covered.Should().OnlyHaveUniqueItems("no entity in two lanes");
    covered.ToHashSet().Should().BeEquivalentTo(all, "every content entity has exactly one fate");
    lanes.Support.Should().Contain("SupportTickets");
    lanes.Statutory.Should().Contain("Transactions");
}
```

- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement** `StatutoryRetentionSet.Tables` (the financial subset) + `ErasureLanes.Classify` (support = the support tables; statutory = `StatutoryRetentionSet ∩ content`; purge = the rest). **Consult the user / models.md on exactly which tables are statutory** — record the decision in the code comment citing legal.md § Financial Records.
- [ ] **Step 4: Run — passes. Commit.**

---

### Task 5: `ErasureService` — request (seal + audit + email) + cancel (token-gated)

**Files:**
- Create: `ProjectCeres/Services/ErasureService.cs`, `IErasureService.cs`, `ProjectCeres/Common/Authentication/ErasureTokenGenerator.cs` (mirror `ExportTokenGenerator`)
- Modify: `ProjectCeres/Program.cs` (DI)
- Test: `ProjectCeres.Tests/Integration/Profile/ErasureServiceTests.cs`

**Interfaces:**
- Produces: `Task<ErasureRequest> RequestAsync(CancellationToken)` (creates Sealed request, sets `ApplicationUser.SealedAt`, writes `AuditLogAction.GdprErasureRequested`, generates the cancel token + stores lookup+hash, returns the request + raw token via an out/tuple for the email); `Task<CancelOutcome> CancelAsync(string rawToken, CancellationToken)` (two-factor verify → Status=Cancelled + clear `SealedAt`).

- [ ] **Step 1: Failing tests** — request seals the account + writes the audit row + dedupes an existing Sealed request; cancel with the valid token un-seals + Status=Cancelled; cancel with a wrong token → not-found outcome; cancel after Completed/expired → gone outcome.
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement.** `RequestAsync`: RLS-scoped read for an existing Sealed request (dedupe); else create, set `SealedAt`, generate raw token → `ComputeLookup` + `Hash`, audit via `IAuditLogWriter`. `CancelAsync`: **admin-context lookup by `CancelTokenLookup`** (the caller is sealed → can't authenticate, so this is a pre-auth token confirm like `LockoutUnlockService.ConfirmAsync` — read it; `[RlsBypassJustified]` + `IgnoreQueryFilters`), then `Verify` the hash, then flip Status + clear `SealedAt`.
- [ ] **Step 4: Run — passes. Commit.**

---

### Task 6: `ErasureExecutor` — the three-lane sequence

**Files:**
- Create: `ProjectCeres/Services/ErasureExecutor.cs`
- Test: `ProjectCeres.Tests/Integration/Profile/ErasureExecutorTests.cs`

**Interfaces:**
- Consumes: `ErasureLanes.Classify` (Task 4), `IdentifierRedactor` (Task 3), `AdminDbContext`, the file-deletion service (support attachments + ExportJob ZIP), `IAuditLogWriter`, the erasure pseudonym helper (Task 7).
- Produces: `Task ExecuteAsync(Guid userId, CancellationToken)` — the full §8 sequence, resumable/idempotent.

- [ ] **Step 1: Failing tests (the erasure ship-gate — negative assertions):** seed a user with data across all three lanes + an attachment file + a Ready ExportJob ZIP, run `ExecuteAsync`, then assert:
  - purge-lane tables have zero rows for the user;
  - statutory-lane rows remain but identity is anonymised (a negative assertion: the user's name/email does NOT appear);
  - support threads remain but redacted (the user's email absent from bodies); attachment files deleted;
  - ExportJob ZIP file + row gone;
  - `ApplicationUser.ErasedAt` set, identity fields anonymised;
  - exactly one erasure audit row, user id = the HMAC pseudonym (not the raw id).
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement** the §8 sequence (anonymise-retain → redact-retain support → purge → files → account → audit), each lane check-then-act for idempotency (Task 10 tests the resume). All reads/writes via `AdminDbContext` + `IgnoreQueryFilters().Where(UserId==userId)`; add `ErasureExecutor.cs` to the `ArchitectureTests` IgnoreQueryFilters allow-list with the SweepSessions-style justification.
- [ ] **Step 4: Run — passes. Commit.**

---

### Task 7: Erasure pseudonym helper

**Files:**
- Create: `ProjectCeres/Common/Authentication/ErasurePseudonym.cs`
- Test: `ProjectCeres.Tests/Unit/ErasurePseudonymTests.cs`

**Interfaces:**
- Produces: `string Compute(Guid userId)` — a keyed one-way HMAC (reuse the `TokenLookupHasher` secret/mechanism); same input → same token, non-reversible.

- [ ] **Step 1: Failing test** — deterministic (same id → same output), differs per id, not equal to the raw id string.
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement** via the existing keyed-hash (mirror `TokenLookupHasher.ComputeLookup`). One-line code comment: "local pseudonym; migrates to canonical UserRef at Stage 15 (spec D1)."
- [ ] **Step 4: Run — passes. Commit.**

---

### Task 8: `ErasureWorker` + `--run-erasure-jobs` dispatch + email

**Files:**
- Create: `ProjectCeres/Tools/ErasureWorker.cs`
- Modify: `ProjectCeres/Program.cs` (arg dispatch beside `--run-export-jobs`), `EmailTemplateKey.cs` + EN/ES resx (`GdprErasureInitiated`), `EmailComposer.cs`
- Test: `ProjectCeres.Tests/Integration/Profile/ErasureWorkerTests.cs`, email-composition test

**Interfaces:**
- Consumes: `ErasureService`/`ErasureExecutor`, `IEmailComposer`.
- Produces: `static Task<int> RunAsync(WebApplicationBuilder)` + `ProcessEligibleAsync(...)`.

- [ ] **Step 1:** Add `GdprErasureInitiated` email (enum + composer arm + EN/ES resx with the cancel-link `{0}` placeholder + "cancellable for 72 hours, irreversible after" copy). Compose test EN+ES.
- [ ] **Step 2: Failing worker tests** — a Sealed job past `ExecuteAfter` → executor runs, Status=Completed; a Sealed job WITHIN 72h → NOT executed; a Cancelled job → skipped; the request path sends `GdprErasureInitiated` with a cancel link (fake email service captures it).
- [ ] **Step 3: Implement** `ErasureWorker` (`[RequiresAdminContext]`, SweepSessions shape): claim Sealed-past-ExecuteAfter, re-check Status==Sealed (cancel race), invoke `ExecuteAsync`. `--run-erasure-jobs` dispatch in `Program.cs`.
- [ ] **Step 4: Run — passes. Commit.**

---

### Task 9: `ProfileApiController` erasure endpoints + SPA `/settings/account/erasure` page

**Files:**
- Modify: `ProjectCeres/Controllers/Api/ProfileApiController.cs` (POST `/erasure`, POST `/erasure/cancel`), `Program.cs` (a `ProfileErasureByUser` rate-limit policy)
- Create (SPA, VIA frontend-orchestrator): `ProjectCeres.Client/src/app/features/account/ErasurePage.tsx` (+ api helper, route, test), an E2E spec
- Test: `ProjectCeres.Tests/Integration/Profile/ProfileErasureApiTests.cs`

- [ ] **Step 1: Failing API tests** — POST `/erasure` without recent auth → 401; wrong typed-confirm → 422; success → 202 + account sealed; cancel with valid token → 204 + un-sealed; cancel wrong token → 404; cancel after execute → 410.
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement** the two endpoints (POST `/erasure` `[Authorize][RequireRecentAuth]` + typed-confirm→422; POST `/erasure/cancel` `[AllowAnonymous]`, token in body). Rate-limit policy mirrors `ProfileExportByUser`.
- [ ] **Step 4: Run — passes. Commit the backend.**
- [ ] **Step 5: SPA page via `frontend-orchestrator`** (Phase 1 new surface): `/settings/account/erasure` — an `AlertDialog` destructive-confirm (the SPA confirm standard; NOT `confirm()`/`ConfirmDialog`), a typed-"ERASE" input, plain-English "what is deleted / what is retained (statutory financial records, a redacted support record) / when (72h hold)", reauth via `useStepUp`, on confirm POST `/api/profile/erasure` → toast "Erasure scheduled — check your email; you have 72 hours to cancel." Linked from `/settings/account`. Design-system checklist + E2E-verify (request→sealed→cancel-link restores; the 72h/irreversible copy present). **Show the rendered result + get explicit user approval before commit** (frontend gate).

---

### Task 10: Worker idempotency / resume + cancel-race tests

**Files:**
- Test: extend `ProjectCeres.Tests/Integration/Profile/ErasureExecutorTests.cs` / `ErasureWorkerTests.cs`

- [ ] **Step 1: Failing tests** — a crash mid-execute (simulate: run the executor, kill after the support lane) then re-run → completes without error, no double-delete; a job Cancelled after claim but before execute → executor skips it (re-check Status). Idempotency: deleting an already-gone file is a no-op; anonymising an already-anonymised row is a no-op.
- [ ] **Step 2–4:** Harden the executor's check-then-act per lane until green. **Commit.**

---

### Task 11: BDD + docs sync + roadmap close + legal.md sign-off

**Files:**
- Create: `ProjectCeres.Specs/Features/Erasure.feature` + steps
- Modify: `docs/api-contract.md`, `docs/models.md`, `docs/security-model.md`, `docs/roadmap-phase-three.md` §13.9, `CHANGELOG.md`
- **Surface for user sign-off:** `docs/legal.md` (READ-ONLY — do NOT edit without explicit approval)

- [ ] **Step 1: BDD scenario** (Reqnroll, mirror the 13.8 golden path): request erasure → cancel link restores the account; AND request → worker after 72h → login refused + purge-lane data gone + statutory rows anonymised.
- [ ] **Step 2: sync-docs** across the branch diff (endpoints, `ErasureRequest`/`ApplicationUser` columns, seal enforcement, three-lane model).
- [ ] **Step 3: changelog-sync** (user-facing: "erase your account and data").
- [ ] **Step 4: Roadmap §13.9** — tick the checklist items as-built; the ExportJob-ZIP-on-erasure `[ ]` (added in 13.8) is satisfied by Task 6 → tick it.
- [ ] **Step 5: legal.md — STOP and get user sign-off** on two additions: (a) erasure has a 72h non-restorable *cancel-only* hold, distinct from churn's restorable archive (D4); (b) the legitimate-interest basis for retaining redacted, de-identified support-knowledge records (B3a). Draft the exact wording, present it, wait for explicit yes, then apply. **Commit.**

---

## Notes for the executor
- **Tasks 1 + the migration** trip the reviewer-pipeline + rls-audit + registry-sweep evidence gates (IUserOwned + migration + ApplicationUser columns). Budget for the 3-agent security pass — this is auth + destructive-delete code, the highest-risk in the stage; give the executor + seal-enforcement a dedicated security review.
- **The executor (Task 6) is the crown jewel** — its negative assertions (statutory rows anonymised not deleted; support redacted not purged; pseudonymised audit) are the ship-gate. A missing `.Where(UserId==)` on any lane = a cross-user delete. Review it hard.
- **`legal.md` never gets edited without the user's explicit yes** (Task 11 step 5) — it is read-only per the doc rules.
- Confirm the exact `StatutoryRetentionSet` membership with the user / `legal.md` § Financial Records before Task 4 — which tables carry Código de Comercio/LGT records is a legal call, not a guess.
