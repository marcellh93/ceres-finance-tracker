# Stage 13.8 — Data Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user request an asynchronous ZIP archive of all their own data (GDPR access/portability), delivered via a one-time, 24h-expiring emailed download link.

**Architecture:** A reauth-gated `POST /api/profile/export` writes a durable user-owned `ExportJob` row (Pending) and returns `202`. A poll-on-timer cron worker (`--run-export-jobs`, the `SweepSessions` pattern) claims Pending jobs through `AdminDbContext` (BYPASSRLS), builds the ZIP on the filesystem, stores an HMAC token fingerprint, and emails the link. `GET /api/profile/export/download?token=…` (login AND token) streams the ZIP under the caller's own RLS scope and stamps it consumed. A new `/settings/account` SPA page hosts the export button.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core + Npgsql / PostgreSQL RLS; xUnit + Reqnroll; React 19 + Vite + TypeScript + shadcn/base-ui + sonner.

**Spec:** `docs/superpowers/specs/2026-09-19-stage-13-8-data-export-design.md`

## Global Constraints

- **Module route is `api/profile`** — NOT `api/account` (one-char collision with `api/accounts`, the financial-accounts resource). Export is a scoped action: `POST /api/profile/export`, `GET /api/profile/export/download`.
- **`ExportJob` is user-owned** (`IUserOwned`, `Guid UserId`) → the full **5-registry rule** applies: `DbSet` on `AppDbContext`; `OnModelCreating` config + unique index on `TokenLookup`; RLS migration enabling `user_isolation`; membership implicit via `UserOwnedModel.RlsTables` (model-derived — no hand list to edit, but the RLS migration must add the policy); DI for `ExportJobService`. The `verify-stage-completeness` evidence gate enforces this on the turn that commits the model.
- **No raw token stored** — only `TokenLookup = _lookupHasher.ComputeLookup(rawToken)` (Stage 6.15 pattern). Raw token lives only in the emailed URL.
- **Files on the filesystem, never DB BLOBs** — the ZIP and all attachment reads go through paths (`StoredPath`), mirroring the attachment convention.
- **Success status `202 Accepted`** with the api-contract.md body shape: `{ "data": { "jobId": "…", "message": "…" } }`. ModelState/validation failures use the project's `422` convention (`UnprocessableEntity`), never 400.
- **Download requires login AND token** (D6). `GET /download` is `[Authorize]` (NOT `[RequireRecentAuth]`); `POST /export` is `[Authorize]` + `[RequireRecentAuth]`.
- **Rate limit 1/24h/user** via a `ProfileExportByUser` policy following the existing `*ByUser` partition idiom.
- **Content = financial + attachments + profile; exclude security/audit** (D4). See Task 5's enumeration note — the list is `UserOwnedModel.FinanceTables(model)` **plus SupportTickets/SupportMessages** (FinanceTables excludes SupportTickets for a legacy-remap reason, not a content reason).
- **Worker = poll-on-timer**, `dotnet run -- --run-export-jobs`, `SweepSessions`/`AdminDbContext` pattern. External cron triggers it.
- **No `Co-Authored-By` trailer** on any commit. Stay on `main`.
- **`UserContentEntities` is the single-sourced seam** Stage 13.9 erasure reuses.

---

### Task 1: `ExportJob` entity + enums + 5-registry

**Files:**
- Create: `ProjectCeres/Models/ExportJob.cs`
- Modify: `ProjectCeres/Data/AppDbContext.cs` (DbSet + `OnModelCreating` config; near the token configs ~line 273–314)
- Test: `ProjectCeres.Tests/Integration/Rls/ExportJobRlsTests.cs`

**Interfaces:**
- Produces: `ExportJob` (`IUserOwned`), `ExportJobStatus { Pending, Processing, Ready, Failed }`, `ExportFormat { Zip }`. `AppDbContext.ExportJobs` DbSet; unique index on `TokenLookup`.

- [ ] **Step 1: Write the entity**

```csharp
// ProjectCeres/Models/ExportJob.cs
using ProjectCeres.Common;

namespace ProjectCeres.Models;

public enum ExportJobStatus { Pending = 0, Processing = 1, Ready = 2, Failed = 3 }
public enum ExportFormat { Zip = 0 }

public sealed class ExportJob : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ExportJobStatus Status { get; set; } = ExportJobStatus.Pending;
    public ExportFormat Format { get; set; } = ExportFormat.Zip;
    public DateTime RequestedAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public int FailureCount { get; set; }
    public string? StoredPath { get; set; }
    // HMAC-SHA256(serverSecret, rawToken); unique. Empty until the worker builds the ZIP.
    public byte[] TokenLookup { get; set; } = Array.Empty<byte>();
    public DateTime? EmailedAt { get; set; }
}
```

- [ ] **Step 2: Register the DbSet + config** in `AppDbContext.cs` (mirror the `LockoutUnlockToken` block at ~309):

```csharp
public DbSet<ExportJob> ExportJobs => Set<ExportJob>();
// in OnModelCreating, beside the other token configs:
modelBuilder.Entity<ExportJob>(b =>
{
    b.HasKey(e => e.Id);
    b.Property(e => e.TokenLookup).IsRequired();
    // Filtered unique index: many rows can share the empty pre-Ready lookup, so only
    // enforce uniqueness on non-empty fingerprints.
    b.HasIndex(e => e.TokenLookup).IsUnique().HasFilter("octet_length(\"TokenLookup\") > 0");
    b.HasIndex(e => new { e.UserId, e.Status });
});
```

- [ ] **Step 3: Write the failing RLS test** (mirror `ProjectCeres.Tests/Integration/Rls/` group-1 style — a foreign row is invisible under `ceres_app`):

```csharp
// asserts User B cannot see User A's ExportJob via a raw query under B's scope
[Fact]
public async Task ExportJob_is_invisible_across_users_under_rls() { /* seed A's job via admin ctx; query as B; assert empty */ }
```

- [ ] **Step 4: Run — fails** (`ExportJobs` DbSet exists but no RLS policy yet → B sees A's row). `dotnet test --filter ExportJob_is_invisible_across_users_under_rls`
- [ ] **Step 5: Create the RLS migration** (next task) — leave the test red until Task 2.
- [ ] **Step 6: Commit** — `git add … && git commit -m "feat(13.8): ExportJob user-owned entity + DbSet config"`

---

### Task 2: RLS migration for `ExportJobs`

**Files:**
- Create: migration via `dotnet ef migrations add AddExportJobs`
- Modify: the generated migration to add the `user_isolation` RLS policy (mirror `AddRowLevelSecurityPolicies`)

**Interfaces:**
- Consumes: `ExportJob` from Task 1.
- Produces: `ExportJobs` table with RLS enabled + forced.

- [ ] **Step 1:** `dotnet ef migrations add AddExportJobs --project ProjectCeres`
- [ ] **Step 2:** In the migration `Up()`, after the auto-generated `CreateTable`, append the RLS block (copy the per-table pattern from the `AddRowLevelSecurityPolicies` migration verbatim, substituting `ExportJobs`):

```csharp
migrationBuilder.Sql(@"
ALTER TABLE ""ExportJobs"" ENABLE ROW LEVEL SECURITY;
ALTER TABLE ""ExportJobs"" FORCE ROW LEVEL SECURITY;
CREATE POLICY user_isolation ON ""ExportJobs""
  USING (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid)
  WITH CHECK (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid);
");
```
`Down()`: `DROP POLICY IF EXISTS user_isolation ON "ExportJobs";` then the auto-generated `DropTable`.

- [ ] **Step 3:** `dotnet ef database update` (dev DB).
- [ ] **Step 4:** Re-run the Task-1 RLS test → now PASSES (B cannot see A's row). Also run the RLS **parity** test (`pg_policies` set match) so the new table is covered.
- [ ] **Step 5: Commit** — `git commit -m "feat(13.8): AddExportJobs migration with RLS user_isolation"`

---

### Task 3: `UserContentEntities` seam (the 13.9-shared list)

**Files:**
- Create: `ProjectCeres/Common/UserContentEntities.cs`
- Test: `ProjectCeres.Tests/Unit/UserContentEntitiesTests.cs`

**Interfaces:**
- Produces: `UserContentEntities.List(IReadOnlyModel model): IReadOnlyList<UserOwnedTable>` — the entities that count as "the user's own content" for export (and, later, erasure).

**Enumeration note (must-read):** `UserOwnedModel.FinanceTables` **excludes `SupportTickets`** — but for a *legacy sentinel-remap* reason (see its `AuthInternalTables` comment), NOT because support tickets are security-internal. Support tickets/messages ARE user content and MUST be in the export. So this list is `FinanceTables` **plus** the support tables — not `FinanceTables` alone.

- [ ] **Step 1: Write the failing test:**

```csharp
[Fact]
public void List_includes_support_tickets_but_excludes_security_tables()
{
    var model = /* build via a throwaway AppDbContext model */;
    var names = UserContentEntities.List(model).Select(t => t.PostgresTableName).ToHashSet();
    names.Should().Contain("SupportTickets");                 // user content — must export
    names.Should().Contain("Accounts");
    names.Should().NotContain("AuditLogs");                    // security — excluded
    names.Should().NotContain("FailedLoginAttempts");
    names.Should().NotContain("UserSessions");
}
```

- [ ] **Step 2: Run — fails** (class missing).
- [ ] **Step 3: Implement:**

```csharp
// ProjectCeres/Common/UserContentEntities.cs
using Microsoft.EntityFrameworkCore.Metadata;

namespace ProjectCeres.Common;

/// <summary>
/// Single source of truth for "the user's own content" — the entities a data export
/// enumerates and (Stage 13.9) an erasure deletes. = FinanceTables + the support
/// tables (FinanceTables excludes SupportTickets for a legacy sentinel-remap reason,
/// not a content reason; support correspondence IS user content). Excludes all
/// security/auth-internal tables (audit, failed-login, sessions, tokens).
/// </summary>
public static class UserContentEntities
{
    private static readonly HashSet<string> SupportContentTables =
        new(StringComparer.Ordinal) { "SupportTickets", "SupportMessages", "SupportTicketAttachments" };

    public static IReadOnlyList<UserOwnedTable> List(IReadOnlyModel model)
    {
        var finance = UserOwnedModel.FinanceTables(model).ToList();
        var have = finance.Select(t => t.PostgresTableName).ToHashSet(StringComparer.Ordinal);
        var support = UserOwnedModel.RlsTables(model)
            .Where(t => SupportContentTables.Contains(t.PostgresTableName) && !have.Contains(t.PostgresTableName));
        return finance.Concat(support)
            .OrderBy(t => t.PostgresTableName, StringComparer.Ordinal).ToList();
    }
}
```

- [ ] **Step 4: Run — passes.**
- [ ] **Step 5: Commit** — `git commit -m "feat(13.8): UserContentEntities seam (export/erasure single source)"`

---

### Task 4: `ExportJobService` — create (dedupe + rate check) + read-own

**Files:**
- Create: `ProjectCeres/Services/ExportJobService.cs`, `IExportJobService.cs`
- Modify: `ProjectCeres/Program.cs` (DI registration, `AddScoped`)
- Test: `ProjectCeres.Tests/Integration/Profile/ExportJobServiceTests.cs`

**Interfaces:**
- Consumes: `ExportJob` (Task 1), `AppDbContext`, `ICurrentUserAccessor`, `TimeProvider`.
- Produces:
  - `Task<ExportJob> CreateOrGetPendingAsync(CancellationToken ct)` — returns the caller's existing Pending/Processing job if one exists, else inserts a new Pending row (under the caller's own RLS scope).
  - `Task<ExportJob?> FindOwnByTokenAsync(byte[] tokenLookup, CancellationToken ct)` — the caller's own job matching the fingerprint (RLS makes foreign rows invisible → null).

- [ ] **Step 1: Failing test** — `CreateOrGetPendingAsync` twice in a row yields the same job id (dedupe), and a fresh user with no job gets a new Pending row.
- [ ] **Step 2: Run — fails** (service missing).
- [ ] **Step 3: Implement** the two methods. `CreateOrGetPendingAsync`: query `db.ExportJobs` (RLS-scoped, no `IgnoreQueryFilters`) for `Status is Pending or Processing`; if found return it; else add `new ExportJob { Id = Guid.NewGuid(), UserId = currentUser.UserId, Status = Pending, Format = Zip, RequestedAt = now }` and save. `FindOwnByTokenAsync`: `FirstOrDefaultAsync(e => e.TokenLookup == tokenLookup)` (RLS-scoped).
- [ ] **Step 4: Run — passes.**
- [ ] **Step 5: Commit.**

---

### Task 5: `DataExportBuilder` — entities → CSVs + attachments + manifest → ZIP

**Files:**
- Create: `ProjectCeres/Services/DataExportBuilder.cs`
- Test: `ProjectCeres.Tests/Integration/Profile/DataExportBuilderTests.cs`

**Interfaces:**
- Consumes: `UserContentEntities.List` (Task 3), `CsvFormattingHelper`, `AdminDbContext` (the builder runs in the worker, cross-user — it is handed a specific `userId` and reads that user's rows via `IgnoreQueryFilters().Where(UserId == userId)`), the attachment file paths.
- Produces: `Task<string> BuildAsync(Guid userId, string outputDir, CancellationToken ct)` — writes a ZIP to `outputDir`, returns its path.

- [ ] **Step 1: Failing tests** (the content ship-gate — negative assertions):
  - ZIP contains `accounts.csv`, `transactions.csv`, …, `profile.csv`, `manifest.txt`.
  - ZIP does **NOT** contain any file named for `AuditLogs`/`FailedLoginAttempts`/`UserSessions` (D4 boundary — negative assertion).
  - A transaction description of `=CMD()` appears escaped (`'=CMD()` or quoted) in `transactions.csv` (injection).
  - Each CSV begins with the UTF-8 BOM (`EF BB BF`).
  - `attachments/` contains the file bytes for a seeded `TransactionAttachment`.
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement.** For each `UserContentEntities.List(model)` entry: query rows for `userId` via admin ctx + `IgnoreQueryFilters`, render CSV via `CsvFormattingHelper` (BOM + escaping), add a ZIP entry `<table>.csv`. Add `profile.csv` (user display name, email, Settings row). Copy each attachment file (`StoredPath`) into `attachments/`. Write `manifest.txt` listing entries + `DateTimeOffset.UtcNow`. Use `System.IO.Compression.ZipArchive`.
- [ ] **Step 4: Run — passes.**
- [ ] **Step 5: Commit.**

---

### Task 6: `ExportJobWorker` + `--run-export-jobs` dispatch

**Files:**
- Create: `ProjectCeres/Tools/ExportJobWorker.cs`
- Modify: `ProjectCeres/Program.cs` (arg dispatch, beside `--sweep-sessions` ~line 628)
- Test: `ProjectCeres.Tests/Integration/Profile/ExportJobWorkerTests.cs`

**Interfaces:**
- Consumes: `AdminDbContext`, `DataExportBuilder` (Task 5), `TokenLookupHasher`, an `ITokenGenerator` (`Generate()`/`Hash()`), `IEmailComposer` + `IEmailService`, the export directory config, `TimeProvider`.
- Produces: `static Task<int> RunAsync(WebApplicationBuilder builder)` (SweepSessions shape) + `static Task ProcessPendingAsync(...)` for testability.

- [ ] **Step 1: Failing tests:**
  - A Pending job → after `ProcessPendingAsync`, its `Status == Ready`, `TokenLookup` non-empty, `StoredPath` set, `ReadyAt`/`ExpiresAt` set, `EmailedAt` set, one email sent.
  - **Idempotency:** a `Ready` job with `EmailedAt == null` (crash-after-build) → re-run re-sends the email, does NOT rebuild the ZIP.
  - **Retry/fail:** a builder that throws increments `FailureCount`; on the 3rd failure `Status == Failed` + a "please retry" email; partial ZIP deleted.
  - **Cleanup:** a job past `ExpiresAt` (or `ConsumedAt` set) → ZIP file deleted, `StoredPath` nulled.
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement** `ProcessPendingAsync` (claim Pending → Processing via admin ctx + `IgnoreQueryFilters`; build; tokenize with `Generate()`/`ComputeLookup()`; set Ready + timestamps; compose+send `GdprExportReady` in try/catch, stamp `EmailedAt` on success) and the re-send / retry / cleanup branches. `RunAsync` mirrors `SweepSessions.RunAsync` (build app, scope, resolve deps, log count, return 0).
- [ ] **Step 4:** Add the dispatch in `Program.cs`:

```csharp
// Invocation: dotnet run --project ProjectCeres -- --run-export-jobs
if (args.Length > 0 && args[0] == "--run-export-jobs")
{
    Environment.Exit(await ProjectCeres.Tools.ExportJobWorker.RunAsync(builder));
}
```

- [ ] **Step 5: Run — passes. Commit.**

---

### Task 7: Email template `GdprExportReady` (EN + ES)

**Files:**
- Modify: `ProjectCeres/Common/Email/EmailTemplateKey.cs` (+ `GdprExportReady`)
- Modify: the EN + ES resx pairs (mirror an existing template key's entries)
- Test: `ProjectCeres.Tests` email-composition test (both cultures compose without a missing-key throw)

- [ ] **Step 1: Failing test** — `Compose(EmailTemplateKey.GdprExportReady, culture, downloadUrl)` composes for `en` and `es`.
- [ ] **Step 2: Run — fails** (key/resx missing).
- [ ] **Step 3:** Add the enum value + EN/ES subject+body resx entries (body includes the download URL + a "link valid for 24 hours, single use" line).
- [ ] **Step 4: Run — passes. Commit.**

---

### Task 8: `ProfileApiController` — `POST /export` + `GET /export/download`

**Files:**
- Create: `ProjectCeres/Controllers/Api/ProfileApiController.cs`
- Modify: `ProjectCeres/Program.cs` (add the `ProfileExportByUser` rate-limit policy beside the other `*ByUser` policies ~line 433)
- Test: `ProjectCeres.Tests/Integration/Profile/ProfileExportApiTests.cs`

**Interfaces:**
- Consumes: `IExportJobService` (Task 4), `TokenLookupHasher`, the export directory config.

- [ ] **Step 1: Failing tests** (the API ship-gate — every negative assertion from spec §10):
  - `POST /export` without recent auth → `401 REAUTH_REQUIRED`.
  - `POST /export` twice in 24h → 2nd is `429` with `Retry-After`.
  - `POST /export` while a Pending job exists → returns the same job id (dedupe), `202`.
  - `POST /export` success → `202` with `{ "data": { "jobId", "message" } }`.
  - `POST /export` with `{"format":"json"}` → `422`.
  - `GET /download?token=…` valid + logged in → `200` file stream.
  - `GET /download` valid token but **not logged in** → `401` (D6 dual gate).
  - `GET /download` with a **consumed** token → `410`.
  - `GET /download` with an **expired** job → `410`.
  - `GET /download` with **another user's** token → `404` (RLS invisibility, not 403).
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement** the controller. `[Route("api/profile")]`. `POST("export")` `[Authorize][RequireRecentAuth][EnableRateLimiting("ProfileExportByUser")]`: validate format (else `UnprocessableEntity`), `CreateOrGetPendingAsync`, return `Accepted(new { data = new { jobId, message } })`. `GET("export/download")` `[Authorize]`: compute lookup, `FindOwnByTokenAsync` (null → 404), check `Status==Ready` (else 404), not expired + `ConsumedAt==null` (else 410), stamp `ConsumedAt`, `PhysicalFile(StoredPath, "application/zip", downloadName)`.
- [ ] **Step 4:** Add the `ProfileExportByUser` policy (per-user partition, 1 permit / 24h window; copy the `*ByUser` `AuthenticateAsync`-then-partition idiom from `AuthReauthByUser`).
- [ ] **Step 5: Run — passes. Commit.**

---

### Task 9: BDD golden-path scenario (Reqnroll)

**Files:**
- Create: `ProjectCeres.Specs/Features/DataExport.feature` + step defs

- [ ] **Step 1:** Write the scenario:

```gherkin
Scenario: A user exports their data and downloads it once
  Given a signed-in user with recent re-authentication
  When they request a data export
  Then the request is accepted with status 202
  When the export worker runs
  Then they receive an export-ready email with a download link
  When they follow the link while signed in
  Then the ZIP downloads successfully
  When they follow the same link again
  Then the download is refused as already used
```

- [ ] **Step 2:** Step defs reuse the `SpecsAuthFactory` harness (Stage 12.15). Drive `POST /api/profile/export`, invoke `ExportJobWorker.ProcessPendingAsync`, read the captured email link, `GET` the download twice.
- [ ] **Step 3: Run** `dotnet test ProjectCeres.Specs` → passes. **Commit.**

---

### Task 10: `/settings/account` SPA page + export action

**Files:**
- Create: `ProjectCeres.Client/src/app/pages/Account.tsx` (or `features/account/AccountPage.tsx` matching the settings-feature layout)
- Modify: `ProjectCeres.Client/src/app/App.tsx` (add `<Route path="settings/account" element={<Account />} />`)
- Modify: `ProjectCeres.Client/src/app/features/…/settings-api.ts` (or a new `profile-api.ts`) — `requestExport()` → `POST /api/profile/export`
- Test: `Account.test.tsx` (Vitest + RTL)

> **Frontend routing note:** this task creates a new SPA surface → per CLAUDE.md it must be built THROUGH the `frontend-orchestrator` skill (discovery → build → refine), not by editing `.tsx` directly here. This plan task is the backend-contract handoff; the orchestrator owns the visual build, the design-system audit, and the E2E verification.

- [ ] **Step 1: Failing test** — the page renders an "Export my data" button; clicking it calls `requestExport()` and shows a success toast.
- [ ] **Step 2: Run — fails.**
- [ ] **Step 3: Implement** (via `frontend-orchestrator`): `Account` page with a `<Button>` (existing primitive; `max-sm:h-11` gives the 44px mobile target); on click, step-up via the existing `useStepUp` auto-replay, `POST /api/profile/export`, then `toast.success("Export started — we'll email you when it's ready.")` (sonner). Copy wraps cleanly at 375px.
- [ ] **Step 4: Run — passes.**
- [ ] **Step 5:** E2E (Playwright) — request export from `/settings/account`, assert the 202 confirmation toast + 44px button + no 375px overflow (roadmap 13.8 verification items). **Commit.**

---

### Task 11: Docs sync + roadmap close

**Files:**
- Modify: `docs/api-contract.md` (the two `api/profile/export*` endpoints + codes), `docs/models.md` (`ExportJob`), `docs/security-model.md` (download dual-gate + export-file handling), `docs/roadmap-phase-three.md` §13.8 checklist.

- [ ] **Step 1:** Run `sync-docs` against the branch diff; apply routed updates.
- [ ] **Step 2:** Run `changelog-sync` (Added: data export).
- [ ] **Step 3:** Tick the §13.8 roadmap verification items now covered by tests; leave browser-only items for the E2E pass. **Commit.**

---

## Notes for the executor
- **`ExportJob` touches an IUserOwned model + a migration** → the turn that commits Tasks 1–2 triggers the **reviewer-pipeline** evidence slot (auth/migration/IUserOwned diff) + `rls-audit` + `registry-sweep`. Budget for that.
- **The download endpoint is the highest-risk surface** (hands over a full personal-data archive) — its IDOR/consumed/expired/not-logged-in tests (Task 8) are load-bearing ship-gate assertions and must all pass before the stage closes.
- **Export directory** needs a config key (e.g. `Export:Directory`) with a Production value and a gitignored local default, mirroring the attachment `uploads/` dir. Confirm the path convention against the attachment storage before Task 5.
