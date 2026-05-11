# Stage 6.14 — Audit Log Entity + Writer (Design Spec)

> **Diataxis type:** Reference — design for a single roadmap sub-stage, consumed by `superpowers:writing-plans` next.
>
> **Roadmap row:** `docs/roadmap-phase-three.md` § Stage 6 sub-stages — row 6.14 ("Audit log table (`AuditLog` entity + writer service)").
>
> **Sequencing:** Lands inside Stage 6. The 6-month retention purge job, the global query filter, and the FK to `AspNetUsers.Id` are all Stage 7+ (see § 8). The `GET /api/auth/audit-log` controller is Stage 12 (see § 8). The DB-level `INSERT-only` runtime role grant is Stage 16 (see § 8).
>
> **Stage 6 close-out dependency:** Stage 6.14 ships the writer + call-site coverage; the close-out flow diagrams in `security-model.md` (roadmap line 573) overlay audit-log writes on the request flowcharts AFTER 6.14 lands.

---

## 1. Purpose

Record every security-relevant authentication event in an append-only `AuditLog` table so the user (and, later, a support admin) has a durable trail of what happened to their account. The table answers questions like "when did I last log in?", "did somebody else change my email?", "when were my backup codes regenerated?" — questions that `FailedLoginAttempt` doesn't cover (`FailedLoginAttempt` is for *rejected* attempts; `AuditLog` is for *successful* state changes plus a handful of request events).

This stage ships:

1. The `AuditLog` entity + migration.
2. The `IAuditLogWriter` abstraction + `AuditLogWriter` implementation.
3. Call-site wiring into 13 existing flows (§ 4).
4. Tests (~24) — unit, integration per call site, architecture (§ 5).
5. Doc updates (§ 6).

This stage explicitly does **not** ship: a read endpoint, a UI page, a retention purge, the financial-event call sites, or the DB-level `INSERT-only` grant. Each is sequenced into a later stage and listed in § 8.

---

## 2. Decisions (locked during brainstorm)

| # | Decision | Rationale |
|---|---|---|
| D1 | Retention: **6 months** per-user fan-out via `IUserJobRunner`. | Aligns with `planning-phase3.md` § Audit log entity + writer (line 451) and roadmap line 1273. `security-model.md` § Data retention table line 939 currently says "1 year" — fixed in this stage's doc-sync (see § 6). |
| D2 | Financial events (`TransactionCreated/Deleted`, `TransferCreated/Deleted`) **deferred to Stage 7**. | Stage 7's multi-tenancy cutover already touches every financial-service method to add the `UserId` scope; that's the natural moment to add the `IAuditLogWriter` calls. Keeps 6.14 focused on auth events. Per security-model.md line 1040, financial events are required before public launch, not before Stage 7. |
| D3 | Writer failure posture: **loud-failure on the request hot path**, fresh `IServiceScopeFactory`-resolved `DbContext`. Mirrors `FailedLoginRecorder`. | Roadmap preamble (line 5): "loud-failure over silent-failure." A dropped audit row defeats the entire reason the table exists. Mirrors the existing security-recorder pattern in the project. |
| D4 | `EntityType` and `EntityId` are **both nullable, set or null together**. | `UserId` already says "this happened to this user"; `EntityType`/`EntityId` only add value when the event targets a distinct sub-entity (backup-code batch, data-export job). Null on identity-self events (login, logout, register, MFA-disable, password-reset, lockout-unlock). |
| D5 | Read endpoint `GET /api/auth/audit-log` **deferred to Stage 12**. | Stage 12 (Sessions + Support SPA pages) is the natural home — same kind of security-settings read-only surface, and 6.14 has no SPA page to consume it. Stage 6 framing ("no UI yet — integration tests only") supports the deferral. |
| D6 | DB-level `INSERT-only` runtime role grant **deferred to Stage 16**. | Per security-model.md line 1141 ("Database least privilege (DML user)") this is a Phase 3 hosting concern. Stage 16 configures the runtime DB role; until then, application-level discipline (no `UPDATE`/`DELETE` SQL anywhere in code) + the `_BubblesAsException` test guard the table. |

No ADR is required: each decision is consistent with prior architecture (ADR-0065 for the future query filter, ADR-0067 for the future cross-tenant purge, the existing `FailedLoginRecorder` pattern for the writer).

---

## 3. Entity schema

`ProjectCeres.Models.AuditLog`:

| Column | Type (PG) | EF type | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `uuid` | `Guid` | PK, NOT NULL | Writer assigns `Guid.NewGuid()` client-side (matches `FailedLoginRecorder` line 34). |
| `UserId` | `uuid` | `Guid` | NOT NULL | The user the event happened to. **No FK in 6.14.** Stage 7's cutover adds the FK alongside the global query filter, in the same migration as every other user-owned entity. |
| `Action` | `text` | `AuditLogAction` (enum) | NOT NULL | EF `HasConversion<string>()`, matching `FailedLoginReason` (no DB-level CHECK; the .NET enum is the source of truth and an architecture test pins values). |
| `EntityType` | `varchar(64)` | `string?` | nullable | Null on identity-self events. Set together with `EntityId`. |
| `EntityId` | `uuid` | `Guid?` | nullable | Null on identity-self events. Set together with `EntityType`. |
| `OccurredAt` | `timestamp with time zone` | `DateTime` | NOT NULL | UTC wall-clock at write time. |
| `IpAddress` | `varchar(45)` | `string` | NOT NULL, default `"unknown"` | IPv6-sized. `"unknown"` when `Connection.RemoteIpAddress` is null (background contexts, test paths with no HTTP scope). |

**DB-level CHECK constraint:** `CHECK ((entity_type IS NULL AND entity_id IS NULL) OR (entity_type IS NOT NULL AND entity_id IS NOT NULL))`. Belt-and-braces with the application-level guard in `RecordAsync` — catches a hand-written SQL insert or a future migration mistake. This is the only DB-level CHECK on the table.

**Indexes:**

- `IX_AuditLog_UserId_OccurredAt` covering `(UserId, OccurredAt DESC)` — supports the Stage-12 GET endpoint pagination AND the Stage-7+ per-user 6-month purge (`DELETE WHERE UserId = @u AND OccurredAt < @cutoff`).
- `IX_AuditLog_OccurredAt` covering `(OccurredAt)` — defensive, in case a cross-tenant ops query is ever needed.

**Multi-tenancy:** scoped per user. Stage 7's cutover adds the EF global query filter on `UserId` (roadmap line 623 enumerates `AuditLog` in the filter list). Until then, all queries in this stage filter by `UserId` explicitly.

**GDPR on erasure:** `UserId` is **not** nulled — it remains as a pseudonymized identifier per roadmap line 1301. The user-row's PII (email, name) is what gets erased; the audit log retains the user-id as a token whose translation to a human is gone. `IpAddress` is rewritten to `"erased"` in the same erasure transaction (Stage 13 wires this). No `EmailAttempted`-style column exists, so there is no separate PII column to anonymize.

**Retention:** 6-month per-user fan-out cron via `IUserJobRunner` (`DELETE WHERE UserId = @u AND OccurredAt < now() - interval '6 months'`). Ships in Stage 7+ when the cross-tenant runner is available. Different cadence from `FailedLoginAttempt` (1-year flat cross-tenant `DELETE`), called out in `models.md`.

### 3.1 `AuditLogAction` enum

```csharp
public enum AuditLogAction
{
    // Wired in 6.14
    LoginSucceeded,
    LoginSucceededMfa,
    LoginSucceededBackupCode,
    Logout,
    Registered,
    PasswordResetRequested,
    PasswordResetCompleted,
    EmailChangeRequested,
    EmailChangeConfirmed,
    EmailChangeRevoked,
    MfaEnrolled,
    BackupCodesRegenerated,

    // Reserved — call site lands in a later stage
    MfaDisabled,                // wired when an MFA-disable endpoint ships (no disable endpoint in 6.14 codebase)
    LockoutSelfServiceUnlock,   // wired in Stage 6.10
    DataExportRequested,        // wired in Stage 13
    GdprErasureRequested,       // wired in Stage 13
}
```

Reserved values get a one-line comment in the source file ("wired in Stage X") so the gap is obvious to a reader. The architecture test in § 5 pins the enum against this spec list — adding a value without updating this spec fails the build.

---

## 4. Writer service

### 4.1 `IAuditLogWriter`

```csharp
namespace ProjectCeres.Common.Authentication;

public interface IAuditLogWriter
{
    Task RecordAsync(
        Guid userId,
        AuditLogAction action,
        string? entityType = null,
        Guid? entityId = null,
        CancellationToken ct = default);
}
```

### 4.2 `AuditLogWriter` (production implementation)

Location: `ProjectCeres/Common/Authentication/AuditLogWriter.cs` — same folder as `FailedLoginRecorder` (the analog).

Lifetime: **Scoped**. Registered next to `FailedLoginRecorder` in `Program.cs` (the existing registration is at line 125: `builder.Services.AddScoped<FailedLoginRecorder>();` — the new registration is `builder.Services.AddScoped<IAuditLogWriter, AuditLogWriter>();` immediately after).

Dependencies:

- `IServiceScopeFactory` — fresh `DbContext` per call, same rationale as `FailedLoginRecorder`'s docblock (lines 11–18): Identity's `UserStore` can leave the request-scoped `DbContext` with a stale tracked entity after a concurrency race, and a subsequent `SaveChangesAsync` on that contaminated context re-attempts the failed update and silently swallows the new insert. A private scope insulates the writer.
- `IHttpContextAccessor` — for `IpAddress`.

Behaviour of `RecordAsync`:

1. **Invariant guard.** If exactly one of `entityType`/`entityId` is non-null, throw `ArgumentException` before any DB work. Caller bug, fail loud.
2. **Open fresh scope.** `await using var scope = _scopeFactory.CreateAsyncScope();` → resolve `AppDbContext` from `scope.ServiceProvider`.
3. **Build row.** `Id = Guid.NewGuid()`, `UserId = userId`, `Action = action`, `EntityType = entityType`, `EntityId = entityId`, `OccurredAt = DateTime.UtcNow`, `IpAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown"`.
4. **Insert + save.** `db.AuditLogs.Add(entry); await db.SaveChangesAsync(ct);` — exception bubbles, no try/catch.

Failure contract (the explicit "what it does NOT do" pin):

- It does NOT swallow `DbUpdateException` or any other persistence exception.
- It does NOT retry.
- It does NOT enqueue to a background channel.
- It does NOT participate in the caller's `DbContext` transaction (D3 explicitly rejects atomicity-with-effect to keep the writer uniform across events whose state change spans multiple un-coordinated calls).

The integration test `Failed_audit_insert_during_login_returns_500_AND_does_NOT_issue_session_cookie` (§ 5) pins this end-to-end and is the test that future "let's make audit failures soft" PRs must break.

---

## 5. Call sites (retroactive wiring)

Each row below adds `IAuditLogWriter` as a constructor dependency to the named service/controller and calls `RecordAsync` at the trigger point.

| # | Action | Service / location | Trigger point | EntityType / EntityId |
|---|---|---|---|---|
| 1 | `LoginSucceeded` | Login controller, no-MFA branch | After `PasswordSignInAsync` returns `Succeeded` and the application cookie has been issued | null / null |
| 2 | `LoginSucceededMfa` | TOTP verify endpoint | After `VerifyTwoFactorTokenAsync` succeeds and the final sign-in completes | null / null |
| 3 | `LoginSucceededBackupCode` | Backup-code consume endpoint | After the backup-code consume + final sign-in | null / null |
| 4 | `Logout` | Logout endpoint | After `RevokedAt` is stamped on the `UserSession` row and the cookie is cleared | null / null |
| 5 | `Registered` | Register endpoint | After `UserManager.CreateAsync` returns `Succeeded` | null / null |
| 6 | `PasswordResetRequested` | `PasswordResetService.RequestAsync` — **known-email branch only** | After the token row is inserted and the email is queued | null / null |
| 7 | `PasswordResetCompleted` | `PasswordResetService.ConfirmAsync` | After password write + `UpdateSecurityStampAsync` + bulk-revoke all `UserSession` rows, before returning | null / null |
| 8 | `EmailChangeRequested` | `EmailChangeService.RequestAsync` | After both token rows inserted + emails queued | null / null |
| 9 | `EmailChangeConfirmed` | `EmailChangeService.ConfirmAsync` | After `SetEmailAsync` + `SetUserNameAsync` + sibling `RevokeOld` row consumed + sessions revoked + `SecurityStamp` regenerated | null / null |
| 10 | `EmailChangeRevoked` | `EmailChangeService.RevokeAsync` | After both sibling tokens consumed | null / null |
| 11 | `MfaEnrolled` | MFA verify-enroll endpoint | After `SetTwoFactorEnabledAsync(true)` | null / null |
| 12 | `BackupCodesRegenerated` | Backup-codes regenerate endpoint | After the new batch is persisted and the old batch invalidated | `"BackupCodeBatch"` / new-batch id **if the regen produces a distinct batch row; otherwise null / null** (verified during planning by reading the existing regen handler) |

**Why `PasswordResetRequested` is known-email-only:** the unknown-email branch in `PasswordResetService.RequestAsync` already records to `FailedLoginAttempt` with reason `PasswordResetUnknownEmail` (per `FailedLoginAttempt` schema). Writing to `AuditLog` would require a `UserId`, which by definition does not exist on the unknown branch — and inventing a sentinel id would corrupt the per-user purge query in Stage 7+. The negative-assertion test `PasswordReset_request_unknown_email_writes_NO_audit_row` pins this.

**Why `EmailChangeRevoked` is wired:** the revoke flow is a state change that affects the user's security posture (cancels a pending email change), even though the user's `Email` column doesn't move. Roadmap line 553 says "email change" without specifying which step; the spec captures all three of request/confirm/revoke for completeness.

**Reserved-but-not-wired in 6.14:**

- `MfaDisabled` — no disable endpoint exists in the codebase at 6.14. If one ships in this stage's planning phase, this row gets wired and added to the table above; otherwise it remains reserved.
- `LockoutSelfServiceUnlock` — wires in Stage 6.10 (the unlock-token endpoint).
- `DataExportRequested`, `GdprErasureRequested` — wire in Stage 13.

---

## 6. Tests

Three layers. Target: ~24 ship-gate tests (5 unit + 16 integration + 3 architecture).

### 6.1 Unit tests — `ProjectCeres.Tests/Unit/Authentication/AuditLogWriterTests.cs`

| # | Test | Asserts |
|---|---|---|
| U1 | `RecordAsync_InsertsRowWithGivenFields` | Happy path: `userId`/`action`/`entityType`/`entityId` flow through; `OccurredAt` stamped within 5s of `DateTime.UtcNow`; `IpAddress` from a mock `IHttpContextAccessor`. |
| U2 | `RecordAsync_WithNullHttpContext_RecordsIpAsUnknown` | Background contexts (no request thread) get `"unknown"`. |
| U3 | `RecordAsync_WithEntityTypeSetButEntityIdNull_Throws` | Invariant guard, `ArgumentException`. |
| U4 | `RecordAsync_WithEntityIdSetButEntityTypeNull_Throws` | Invariant guard, `ArgumentException`. |
| U5 | `RecordAsync_SaveChangesFailure_BubblesAsException` | Loud-failure contract, mirrors `FailedLoginRecorderTests.SaveChangesFailure_BubblesAs500_DoesNotIssueSession`. |

### 6.2 Integration tests — `ProjectCeres.Tests/Integration/Authentication/AuditLogIntegrationTests.cs`

Each happy-path test exercises the real flow via `WebApplicationFactory` and asserts a single row in `AuditLogs` filtered by the test's own `UserId` (per the memory rule about shared `project_ceres_test` databases).

| # | Test | Notes |
|---|---|---|
| I1 | `Login_no_mfa_writes_LoginSucceeded` | |
| I2 | `Login_mfa_writes_LoginSucceededMfa` | TOTP path |
| I3 | `Login_backup_code_writes_LoginSucceededBackupCode` | Backup-code path |
| I4 | `Logout_writes_Logout` | |
| I5 | `Register_writes_Registered` | |
| I6 | `PasswordReset_request_known_email_writes_PasswordResetRequested` | |
| I7 | `PasswordReset_request_unknown_email_writes_NO_audit_row` | **Negative assertion** — what the writer does NOT do. |
| I8 | `PasswordReset_confirm_writes_PasswordResetCompleted` | |
| I9 | `EmailChange_request_writes_EmailChangeRequested` | |
| I10 | `EmailChange_confirm_writes_EmailChangeConfirmed` | |
| I11 | `EmailChange_revoke_writes_EmailChangeRevoked` | |
| I12 | `Mfa_enroll_verify_writes_MfaEnrolled` | |
| I13 | `BackupCodes_regenerate_writes_BackupCodesRegenerated` | Asserts `EntityType` + `EntityId` if the regen produces a batch row; asserts both null otherwise. Resolved during planning. |

Cross-feature negative assertions:

| # | Test | Asserts |
|---|---|---|
| I14 | `Login_with_wrong_password_writes_NO_audit_row` | Failed auth goes to `FailedLoginAttempt`, not `AuditLog`. |
| I15 | `Login_with_locked_account_writes_NO_audit_row` | Same separation. |
| I16 | `Failed_audit_insert_during_login_returns_500_AND_does_NOT_issue_session_cookie` | The loud-failure contract end-to-end. Mocks `IAuditLogWriter` to throw on `LoginSucceeded`; asserts response is 500, no `__Host-Session` cookie on response, no `UserSession` row inserted. |

### 6.3 Architecture tests — folded into existing `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`

| # | Test | Asserts |
|---|---|---|
| A1 | `IAuditLogWriter_has_exactly_one_production_implementation` | No shadow implementations. |
| A2 | `AuditLogAction_enum_values_match_documented_set` | Pins the enum against this spec's § 3.1 list. Test fails if a value is added/removed without updating the spec. |
| A3 | `AuditLog_entity_contains_no_financial_amount_columns` | Reflection-scan: asserts `AuditLog` has no property named `Amount`, `Balance`, `Value`, `Total`, or any `decimal`/`decimal?` property. Hard-coded list with an inline comment pointing at security-model.md line 554. |

---

## 7. Migration

Filename: `<timestamp>_AddAuditLog.cs`, mirroring `20260509204427_AddFailedLoginAttempt.cs` in shape.

`Up`:

```csharp
migrationBuilder.CreateTable(
    name: "AuditLogs",
    columns: table => new
    {
        Id = table.Column<Guid>(type: "uuid", nullable: false),
        UserId = table.Column<Guid>(type: "uuid", nullable: false),
        Action = table.Column<string>(type: "text", nullable: false),
        EntityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
        EntityId = table.Column<Guid>(type: "uuid", nullable: true),
        OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
        IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: false, defaultValue: "unknown"),
    },
    constraints: table =>
    {
        table.PrimaryKey("PK_AuditLogs", x => x.Id);
        table.CheckConstraint(
            "CK_AuditLog_EntityPair",
            "(\"EntityType\" IS NULL AND \"EntityId\" IS NULL) OR (\"EntityType\" IS NOT NULL AND \"EntityId\" IS NOT NULL)");
    });

migrationBuilder.CreateIndex(
    name: "IX_AuditLog_UserId_OccurredAt",
    table: "AuditLogs",
    columns: new[] { "UserId", "OccurredAt" },
    descending: new[] { false, true });

migrationBuilder.CreateIndex(
    name: "IX_AuditLog_OccurredAt",
    table: "AuditLogs",
    column: "OccurredAt");
```

`Down`: `migrationBuilder.DropTable("AuditLogs");`.

Not in this migration: FK to `AspNetUsers.Id` (Stage 7), `GRANT INSERT / REVOKE UPDATE, DELETE` on the table (Stage 16), the EF global query filter (Stage 7).

---

## 8. Out-of-scope follow-ups (durable record)

| Item | Stage | Why deferred |
|---|---|---|
| 6-month per-user retention purge via `IUserJobRunner` | Stage 7+ | The runner abstraction ships with Stage 7's multi-tenancy cutover (ADR-0067). Until then there is no scheduled background-job infrastructure to fan out on. |
| EF global query filter on `AuditLog.UserId` | Stage 7 | Per ADR-0065, every user-owned filter lands together in the Stage 7 cutover, not piecemeal. |
| FK from `AuditLog.UserId` → `AspNetUsers.Id` | Stage 7 | Same migration as every other user-owned FK at cutover time. |
| `GET /api/auth/audit-log` controller + paginated DTO | Stage 12 | Stage 12 (Sessions + Support SPA pages) is the natural surface — same security-settings read-only pattern. 6.14 has no SPA page to consume the endpoint. |
| `/app/audit-log` SPA page | Stage 12 | Shares the Stage 12 frontend work. |
| Financial events (`TransactionCreated/Deleted`, `TransferCreated/Deleted`) | Stage 7 | Stage 7 already touches every financial-service method to add the `UserId` scope; that's the natural moment to add the `IAuditLogWriter` calls. Required before public launch per security-model.md line 1040. |
| `MfaDisabled` call site | Whenever an MFA-disable endpoint ships | Reserved enum value lives in 6.14. |
| `LockoutSelfServiceUnlock` call site | Stage 6.10 | Reserved enum value lives in 6.14. |
| `DataExportRequested` + `GdprErasureRequested` call sites | Stage 13 | Reserved enum values live in 6.14. |
| Runtime DB role `GRANT INSERT / REVOKE UPDATE, DELETE` on `AuditLogs` | Stage 16 | Per security-model.md line 1141, DB least-privilege is a Phase 3 hosting concern; Stage 16 configures runtime DB roles. Until then, application-level discipline (no `UPDATE`/`DELETE` SQL anywhere in code) + the `_BubblesAsException` test guard the table. |
| Stage 6 close-out flow diagrams overlay audit-log writes on the request flowcharts | Stage 6 close-out (post-6.14) | Per roadmap line 573, the diagrams are a single end-of-stage pass once 6.10, 6.12 ✓, 6.14, and 6.15 have all landed. |

Each row is also written into `docs/planning-phase3.md` § Audit log entity + writer as part of this stage's doc-sync (§ 9), so the deferrals are grep-able from the planning doc, not only from this spec.

---

## 9. Doc updates that ship with the code

In the same commit:

- **`docs/models.md`** — new `AuditLog (Phase 3, Stage 6.14)` section after `FailedLoginAttempt`. Same structure as `FailedLoginAttempt` (schema table, indexes, writer reference, failure contract, multi-tenancy note, GDPR posture, retention).
- **`docs/roadmap-phase-three.md`** — under "Audit logging" (lines 550–555), check `[x]` on the entity, writer, "no financial amounts," items; move the purge-job line into a Stage 7+ annotation. Add a Stage-6.14 banner at the Stage 6 status header (line 448) mirroring 6c.1/6c.2/6.12 banners.
- **`docs/security-model.md`** —
  - Retention table line 939: change "1 year" → "6 months" with a one-line reason ("aligned with planning-phase3.md § Audit log entity + writer Stage 6c sequencing").
  - Line 1040 launch-scope paragraph: split the financial-event extension into a Stage 7 follow-up paragraph so it's clear the financial events are deferred from 6.14, not skipped.
- **`docs/planning-phase3.md`** — under "Audit log entity + writer" (line 451): flip status to "shipped 2026-MM-DD (Stage 6.14)" and append a "Phase 3 launch follow-up:" subsection listing every § 8 row with its target stage. This is the durable home for the deferred decisions (per the standing rule that planning docs are the destination for deferred work, not specs).
- **`docs/api-contract.md`** — line 446: leave the `AuditLog` resource row as-is; add a one-line footnote that the endpoint ships in Stage 12.

---

## 10. Ship-gate checklist

- [ ] `AuditLog` entity in `ProjectCeres.Models`.
- [ ] `AuditLogAction` enum in `ProjectCeres.Models` with `// wired in Stage X` comments on reserved values.
- [ ] Migration `AddAuditLog` matches § 7 (CHECK constraint, two indexes, no FK, no GRANT).
- [ ] `IAuditLogWriter` + `AuditLogWriter` in `ProjectCeres/Common/Authentication/`.
- [ ] DI registration: `builder.Services.AddScoped<IAuditLogWriter, AuditLogWriter>();` in `Program.cs` next to `FailedLoginRecorder`.
- [ ] All 12 call sites in § 5 wired (13 if `MfaDisabled` endpoint exists; verified during planning).
- [ ] 5 unit tests + 16 integration tests + 3 architecture tests green.
- [ ] All doc updates in § 9 in the same commit.
- [ ] `dotnet test` clean. `pnpm test` unchanged.
- [ ] No `Action`, `EntityType`, or `EntityId` value containing a financial amount appears anywhere in production code or tests.
- [ ] `planning-phase3.md` § Audit log entity + writer carries the full § 8 follow-up list.
