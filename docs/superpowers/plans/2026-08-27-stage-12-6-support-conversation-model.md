# Stage 12.6 — Support Conversation Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the conversation-less support ticket shipped in Stage 12.5 with a real two-way conversation: a `SupportTicket` owns an ordered `SupportMessage` collection, a five-status state machine tracks whose turn it is, and both the user and a minimal admin surface can post to the thread.

**Architecture:** A new `SupportMessage : IUserOwned` entity holds the conversation; attachments move from ticket to message. Status is a stored column moved by a static `SupportTicketStateMachine` (rules in the machine, DB writes in the services). The operator replies through a `[RequireAdmin]` endpoint under the `Admin/` namespace using the BYPASSRLS admin context, stamping each agent message with the ticket owner's id read from the loaded ticket. One clean-cutover migration reshapes the 12.5 data. The `/support` SPA page reads the thread in a URL-reflected slide-in sheet.

**Tech Stack:** .NET 10 / ASP.NET Core, EF Core + PostgreSQL (RLS), React 19 + Vite + TypeScript, Vitest, Playwright, xUnit + FluentAssertions + Moq.

**Spec:** `docs/superpowers/specs/2026-08-27-stage-12-6-support-conversation-model-design.md` — read it alongside this plan; the plan argues from the spec and its two-reviewer revision pass.

## Global Constraints

- **Commit straight to `main`.** No branches, no worktrees (project rule). **No `Co-Authored-By` trailer**, no attribution trailer of any kind, in any commit.
- **`SupportMessage` is a new `IUserOwned` entity → the five-registry discipline.** `DbSet` on `AppDbContext`; relationship config in `OnModelCreating`; model-derived `UserOwnedModel.RlsTables` membership (automatic via the `IUserOwned` interface — no manual list); RLS `ENABLE`+`FORCE`+`user_isolation` policy in the same migration that creates the table; no DI (it is an entity). `RlsParityStartupCheck` / `ParityTests` fail the build/boot until the policy lands.
- **BOTH FK hops are composite:** message→ticket `(SupportTicketId, UserId)` → `SupportTicket(Id, UserId)` AND attachment→message `(SupportMessageId, UserId)` → `SupportMessage(Id, UserId)`, both `ON DELETE CASCADE`. `SupportMessage.HasAlternateKey((Id, UserId))`.
- **`SupportMessage.UserId` is always the ticket owner**, even for `AuthorRole=Agent`. Read the owner from the loaded ticket row, **never from the request**.
- **`SupportTicketStateMachine` is a `static class`** (like `ProjectCeres/Services/BudgetPeriod.cs`) — no interface, no DI, no fields. Rules in the machine; DB writes in the services.
- **Operator endpoint lives under the `ProjectCeres/Admin/` namespace** (not just an `api/admin` route), `[RequireAdmin]` at class level, reads the ticket via `AdminDbContext` + `IgnoreQueryFilters()`, stamps `message.UserId = ticket.UserId` **explicitly** before `SaveChanges` (the `UserOwnershipInterceptor` would otherwise stamp the admin), validates the transition before writing.
- **Migration is ONE transaction.** DB-destructive steps run only after **explicit user confirmation** (standing project rule — the executor must pause and ask before applying).
- **Status stored ordinals are the DB contract; do not reorder** the enum: `Open=0, Pending=1, OnHold=2, Solved=3, Closed=4`.
- **Every DB read assertion in a test filters by a unique-to-this-test marker** (the test DB is shared and sequential — `feedback_filter_test_queries_by_test_data`).
- **Never skip/weaken a test to make it pass.** A failing test → fix production, or name the case (`docs/testing.md` § Rules). No `[Fact(Skip=...)]`.
- **Audit uses the interim `AuditLog`** (owner-stamped, `EntityType="SupportTicket"`, `EntityId=ticketId`, new `AuditLogAction.SupportMessageByAgent`) — NOT the deferred `AdminAuditLog` entity (does not exist; stays deferred per roadmap § 15.6).

---

## Test reconciliation inventory (MANDATED FIRST — no code moves before this)

The 12.5 support suites assert the old model (single `Message`, four statuses, attachments-on-ticket, `CloseAsync` as the user's own action). Each is classified below against `docs/testing.md` § Rules. **Task 0 applies this inventory; nothing else starts until it is done.** Verdicts: **migrate** (still valid, compiles + passes as-is after the reshape), **rewrite** (contract intentionally changed — name the change), **retire** (behaviour genuinely deleted).

### `ProjectCeres.Tests/Integration/SupportTicketServiceTests.cs` (15 tests)

| Test | Verdict | Reason |
|---|---|---|
| `CreateAsync_opens_the_ticket_and_stamps_the_current_user` | **rewrite** | Create now also inserts the first `SupportMessage`; assert the message exists, `AuthorRole=User`, owner-stamped, Body = the supplied text. |
| `ListOwnAsync_returns_only_the_callers_tickets_newest_first` | **migrate** | Ownership + ordering unchanged. |
| `CloseAsync_closes_an_open_ticket_and_moves_UpdatedAt` | **migrate** | Close semantics unchanged; still `CloseTicketResult.Closed`. |
| `CloseAsync_refuses_to_close_an_already_closed_ticket` | **migrate** | `CloseTicketResult.AlreadyClosed` unchanged. |
| `CloseAsync_cannot_reach_another_users_ticket` | **migrate** | `CloseTicketResult.NotFound` unchanged. |
| `CreateAsync_links_a_follow_up_to_the_closed_ticket_it_continues` | **rewrite** | Follow-up still linked, but Create now writes a first message; assert both. |
| `CreateAsync_refuses_a_follow_up_to_a_ticket_that_is_still_open` | **migrate** | Follow-up rule unchanged. |
| `CreateAsync_refuses_a_follow_up_to_another_users_ticket` | **migrate** | Follow-up cross-user rule unchanged. |
| `UploadForSupportTicketAsync_writes_the_file_and_stamps_the_owner` | **rewrite** | Upload now targets a MESSAGE, not a ticket; signature and assertions change to `UploadForSupportMessageAsync`. |
| `UploadForSupportTicketAsync_rejects_a_disallowed_type_by_content_not_extension` | **rewrite** | Same, re-pointed to message. |
| `UploadForSupportTicketAsync_rejects_a_file_over_the_10_MB_cap` | **rewrite** | Same, re-pointed to message. |
| `UploadForSupportTicketAsync_caps_a_ticket_at_ten_attachments` | **rewrite** | Cap is now per-message (mirror the transaction cap); re-pointed. |
| `UploadForSupportTicketAsync_refuses_a_write_past_the_per_user_storage_quota` | **rewrite** | Quota unchanged in effect; re-pointed to the message upload. |
| `UploadForSupportTicketAsync_cannot_attach_to_another_users_ticket` | **rewrite** | Re-pointed to message; the composite FK now guards message→attachment. |
| `GetSupportTicketAttachmentAsync_cannot_read_another_users_attachment` | **migrate** | Download owner-scope walks message→ticket→owner; the assertion (foreign attachment unreadable) holds. |

### `ProjectCeres.Tests/Integration/Api/SupportApiTests.cs` (15 tests)

| Test | Verdict | Reason |
|---|---|---|
| `Create_returns_201_with_a_location_header_and_opens_the_ticket` | **rewrite** | 201 + Location unchanged; add that the first message exists. |
| `Create_writes_a_SupportTicketCreated_audit_row_naming_the_ticket` | **migrate** | Create still audits `SupportTicketCreated` keyed on `EntityId`. |
| `List_returns_only_the_callers_own_tickets` | **rewrite** | List shape gains `messageCount` + `lastMessageAt`; assert scoping AND the new fields. |
| `Get_another_users_ticket_is_404_not_403` | **migrate** | IDOR-as-404 unchanged. |
| `Close_closes_the_ticket_and_a_second_close_is_422` | **migrate** | Close status codes unchanged. |
| `Close_another_users_ticket_is_404_and_leaves_it_open` | **migrate** | Unchanged. |
| `Close_an_unknown_ticket_is_also_404` | **migrate** | Unchanged. |
| `Create_links_a_follow_up_to_the_closed_ticket_it_continues` | **migrate** | Wire-binding of `precedingTicketId` unchanged (the first-message add is asserted service-side). |
| `Create_of_a_follow_up_to_another_users_ticket_is_422` | **migrate** | Unchanged. |
| `Create_of_a_follow_up_to_a_still_open_ticket_is_422` | **migrate** | Unchanged. |
| `Upload_then_download_round_trips_the_file` | **rewrite** | Upload route now targets a message; download owner-scope walks message→ticket. |
| `Upload_of_a_disallowed_type_is_422_with_the_friendly_message` | **rewrite** | Re-pointed to the message upload route. |
| `Upload_to_another_users_ticket_is_422` | **rewrite** | Re-pointed; a Closed-ticket message-post also 422 (new). |
| `Download_of_another_users_attachment_is_404` | **migrate** | Owner-scope IDOR unchanged. |
| `A_ticket_with_no_attachment_is_perfectly_valid` | **migrate** | Optional-attachment invariant holds at the message level. |

### `ProjectCeres.Tests/Integration/Api/SupportNotificationTests.cs` (3 tests)

| Test | Verdict | Reason |
|---|---|---|
| `Filing_a_ticket_notifies_the_configured_support_address` | **migrate** | Create→operator email unchanged (`ForConfiguredSupportAddress`). |
| `A_send_failure_does_not_fail_the_ticket` | **migrate** | Best-effort-notify ordering unchanged. |
| `No_configured_address_means_no_notification_but_still_a_ticket` | **migrate** | Unconfigured-address branch unchanged. |

**Net:** 18 migrate, 15 rewrite, 0 retire. No test is deleted; the rewrites all trace to two contract changes (message replaces Message; attachments move to message). New tests are added by the tasks below; the reconciliation only touches existing ones.

---

## File structure

**New files:**
- `ProjectCeres/Models/SupportMessage.cs` — the conversation-message entity.
- `ProjectCeres/Models/SupportMessageAuthor.cs` — the `User | Agent` enum.
- `ProjectCeres/Services/SupportTicketStateMachine.cs` — static transition rules + `TransitionResult`.
- `ProjectCeres/Services/ISupportMessageService.cs` + `SupportMessageService.cs` — user-side reply/thread service.
- `ProjectCeres/Common/Email/SupportNotificationService.cs` — extracted notification logic (3 call sites).
- `ProjectCeres/Admin/SupportAdminApiController.cs` — the operator reply/status endpoint.
- `ProjectCeres/Migrations/<ts>_AddSupportConversationModel.cs` — the one-transaction cutover.
- `ProjectCeres.Tests/Unit/SupportTicketStateMachineTests.cs` — the transition `[Theory]`.
- `ProjectCeres.Tests/Integration/SupportMessageServiceTests.cs` — user-reply service tests.
- `ProjectCeres.Tests/Integration/Api/SupportConversationApiTests.cs` — user thread/reply API.
- `ProjectCeres.Tests/Integration/Admin/SupportAdminApiTests.cs` — operator endpoint tests.
- `ProjectCeres.Client/src/app/features/support/*` — the `/support` page (Task 13, via frontend-orchestrator).

**Modified files:**
- `ProjectCeres/Models/SupportTicket.cs` — drop `Message` + `Attachments`, add `Messages`, `ExternalRef`.
- `ProjectCeres/Models/SupportTicketStatus.cs` — five values.
- `ProjectCeres/Models/SupportTicketAttachment.cs` — `SupportTicketId` → `SupportMessageId`.
- `ProjectCeres/Models/AuditLog.cs` — add `SupportMessageByAgent`.
- `ProjectCeres/Data/AppDbContext.cs` — DbSet + both composite FKs + the message AK.
- `ProjectCeres/Services/SupportTicketService.cs` + `ISupportTicketService.cs` — Create writes first message.
- `ProjectCeres/Services/IFileAttachmentService.cs` + `FileAttachmentService.cs` — support upload targets a message.
- `ProjectCeres/Controllers/Api/SupportApiController.cs` — thread GET, user-reply endpoint, extract notify.
- `ProjectCeres/Common/Email/EmailTemplateKey.cs` + EN/ES resx + `EmailComposer.cs` — two new templates.
- `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` — audit enum set; `mailSendingActions` array; recipient-lock allow-list.
- `docs/models.md`, `docs/api-contract.md`, `docs/security-model.md`, `docs/roadmap-phase-three.md` — sync (Task 15).

---

### Task 0: Apply the test-reconciliation inventory

**Files:**
- Modify: `ProjectCeres.Tests/Integration/SupportTicketServiceTests.cs`, `Api/SupportApiTests.cs`, `Api/SupportNotificationTests.cs`

This task is a no-code-yet checkpoint: it records the inventory decision but does **not** edit tests until the entities exist (the rewrites reference types built in Task 1+). Its deliverable is a committed note.

- [ ] **Step 1: Confirm the inventory above matches the current test files.** Re-run the enumeration: `grep -A1 '\[Fact\]\|\[Theory\]' ProjectCeres.Tests/Integration/SupportTicketServiceTests.cs ProjectCeres.Tests/Integration/Api/SupportApiTests.cs ProjectCeres.Tests/Integration/Api/SupportNotificationTests.cs | grep 'public async Task\|public void'`. If a test exists that is not in the inventory, add a verdict for it before proceeding.

- [ ] **Step 2: Commit the inventory as the plan's authority.** No code change; the inventory lives in this plan file which is already committed. Confirm with `git log --oneline -1 -- docs/superpowers/plans/2026-08-27-stage-12-6-support-conversation-model.md`.

> The actual test edits happen inside the tasks whose entities they depend on: the "rewrite" service tests in Task 6, the "rewrite" API tests in Task 9/10. Task 0 exists so the classification is a deliberate, reviewable gate, not an afterthought.

---

### Task 1: `SupportMessage` entity + `SupportMessageAuthor` enum + status enum widening

**Files:**
- Create: `ProjectCeres/Models/SupportMessage.cs`, `ProjectCeres/Models/SupportMessageAuthor.cs`
- Modify: `ProjectCeres/Models/SupportTicket.cs`, `ProjectCeres/Models/SupportTicketStatus.cs`, `ProjectCeres/Models/SupportTicketAttachment.cs`
- Test: none yet (compile-only; the model is exercised by Task 2's migration + Task 3's DbContext config)

**Interfaces:**
- Produces: `SupportMessage { Guid Id; Guid UserId; Guid SupportTicketId; SupportMessageAuthor AuthorRole; string Body; DateTime CreatedAt; ICollection<SupportTicketAttachment> Attachments; SupportTicket SupportTicket; }`; `enum SupportMessageAuthor { User = 0, Agent = 1 }`; `enum SupportTicketStatus { Open = 0, Pending = 1, OnHold = 2, Solved = 3, Closed = 4 }`; `SupportTicket` gains `ICollection<SupportMessage> Messages` and `string? ExternalRef`, loses `Message` and `Attachments`.

- [ ] **Step 1: Create `SupportMessageAuthor.cs`.**

```csharp
namespace ProjectCeres.Models;

/// <summary>Who wrote a support message. Stored as int; ordinals are the DB contract.</summary>
public enum SupportMessageAuthor
{
    User = 0,
    Agent = 1,
}
```

- [ ] **Step 2: Create `SupportMessage.cs`.**

```csharp
using ProjectCeres.Common;

namespace ProjectCeres.Models;

/// <summary>
/// One message in a support ticket's conversation. IUserOwned and stamped with the TICKET
/// OWNER's id even for Agent messages, so the user reads the whole thread under their own RLS
/// scope; AuthorRole (not ownership) marks a message as the operator's.
/// </summary>
public sealed class SupportMessage : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SupportTicketId { get; set; }

    public SupportMessageAuthor AuthorRole { get; set; } = SupportMessageAuthor.User;
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    public SupportTicket SupportTicket { get; set; } = null!;
    public ICollection<SupportTicketAttachment> Attachments { get; set; } = [];
}
```

- [ ] **Step 3: Widen `SupportTicketStatus.cs` to five values.** Replace the enum body with `Open = 0, Pending = 1, OnHold = 2, Solved = 3, Closed = 4`. Update the XML doc to describe the new states (Pending = waiting on user; OnHold = blocked; Solved = reopenable by user reply; Closed = terminal). Keep the "stored as int, do not reorder" warning.

- [ ] **Step 4: Modify `SupportTicket.cs`.** Remove `public string Message`, remove the `Attachments` navigation. Add `public string? ExternalRef { get; set; }` and `public ICollection<SupportMessage> Messages { get; set; } = [];`. Keep `PrecedingTicketId`, `Priority`, `Status`, timestamps.

- [ ] **Step 5: Modify `SupportTicketAttachment.cs`.** Rename `SupportTicketId` → `SupportMessageId`; rename the `SupportTicket` navigation → `SupportMessage` (type `SupportMessage`). Keep `Id, UserId, FileName, StoredPath, ContentType, FileSizeBytes, UploadedAt`.

- [ ] **Step 6: Build (do NOT run tests — this breaks the build until Task 3 fixes DbContext).**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -c "error CS"`
Expected: NON-zero (references to `.Message`, `.SupportTicketId` on attachment, and the DbContext config break). This is expected; Tasks 2–3 resolve it. **Do not commit yet** — Tasks 1–3 land as one commit because the intermediate state does not build.

---

### Task 2: The cutover migration (data model + data move, one transaction)

**Files:**
- Create: `ProjectCeres/Migrations/<ts>_AddSupportConversationModel.cs` (via `dotnet ef migrations add`)
- Depends on: Task 3's `AppDbContext` config (create the migration AFTER Task 3 so EF scaffolds the schema diff correctly, then hand-edit the data-move + RLS SQL). **Sequence: do Task 3's model config first, then generate this migration.**

**Interfaces:**
- Produces: a migration that creates `SupportMessages` (+ AK, + RLS policy), adds `ExternalRef`, remaps status ints, moves `Message` → first `SupportMessage`, repoints attachments + adds both composite FKs, drops `Message`.

- [ ] **Step 1: (After Task 3) generate the migration.** `dotnet ef migrations add AddSupportConversationModel --project ProjectCeres`. This scaffolds the schema diff (new table, dropped column, changed FK). It will NOT include the data move or the RLS policy — those are hand-added.

- [ ] **Step 2: Hand-edit `Up()` — wrap everything in the migration's implicit transaction and add the RLS policy** (mirror `20260823084630_AddSupportTickets.cs` lines 64–70):

```csharp
migrationBuilder.Sql(@"
    ALTER TABLE ""SupportMessages"" ENABLE ROW LEVEL SECURITY;
    ALTER TABLE ""SupportMessages"" FORCE ROW LEVEL SECURITY;
    CREATE POLICY user_isolation ON ""SupportMessages""
      USING (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid)
      WITH CHECK (""UserId"" = NULLIF(current_setting('app.current_user_ref', true), '')::uuid);
");
```

- [ ] **Step 3: Add the status value-remap as an explicit CASE** (the collision trap — old `Closed=3` must become `4`, not stay at `3` where it now means `Solved`):

```csharp
migrationBuilder.Sql(@"
    UPDATE ""SupportTickets"" SET ""Status"" = CASE ""Status""
        WHEN 0 THEN 0   -- Open       -> Open
        WHEN 1 THEN 0   -- InProgress -> Open
        WHEN 2 THEN 3   -- Resolved   -> Solved
        WHEN 3 THEN 4   -- Closed     -> Closed
        ELSE ""Status"" END;
");
```

Precede it with a pre-flight guard that aborts if an unexpected value is present:

```csharp
migrationBuilder.Sql(@"
    DO $$ BEGIN
      IF EXISTS (SELECT 1 FROM ""SupportTickets"" WHERE ""Status"" NOT IN (0,1,2,3)) THEN
        RAISE EXCEPTION 'AddSupportConversationModel: unexpected SupportTickets.Status value; aborting remap';
      END IF;
    END $$;
");
```

- [ ] **Step 4: Data move — one `SupportMessage` per existing ticket** (before the attachment repoint and the `Message` drop):

```csharp
migrationBuilder.Sql(@"
    INSERT INTO ""SupportMessages"" (""Id"", ""UserId"", ""SupportTicketId"", ""AuthorRole"", ""Body"", ""CreatedAt"")
    SELECT gen_random_uuid(), t.""UserId"", t.""Id"", 0, t.""Message"", t.""CreatedAt""
    FROM ""SupportTickets"" t;
");
```

- [ ] **Step 5: Repoint attachments to their ticket's first message, then let EF's scaffolded FK swap run.** The attachment repoint SQL runs before the scaffolded `DropForeignKey`/`AddForeignKey` for the attachment; ensure ordering by placing it early:

```csharp
migrationBuilder.Sql(@"
    UPDATE ""SupportTicketAttachments"" a
    SET ""SupportMessageId"" = m.""Id""
    FROM ""SupportMessages"" m
    WHERE m.""SupportTicketId"" = a.""SupportTicketId""
      AND a.""UserId"" = m.""UserId"";
");
```

(The column rename `SupportTicketId`→`SupportMessageId` on the attachment table: since EF sees a renamed column it may scaffold a drop+add; verify the scaffold keeps the data by adding the column first, running this UPDATE, then dropping the old column. Hand-order the operations so the UPDATE sits between add-new-column and drop-old-column.)

- [ ] **Step 6: Confirm the scaffold drops `SupportTicket.Message` last**, and that both composite FKs (`SupportMessage → SupportTicket` on `(SupportTicketId, UserId)`, `SupportTicketAttachment → SupportMessage` on `(SupportMessageId, UserId)`) plus `SupportMessage`'s alternate key `(Id, UserId)` are present in the `Up()`. If EF did not scaffold them from the model, add them by hand mirroring `AppDbContext` lines 541–553.

- [ ] **Step 7: Write a matching `Down()`** that reverses cleanly (drop policy, restore `Message` column, move first-message bodies back, re-point attachments to ticket, restore 4-value status, drop `SupportMessages`). A best-effort down is acceptable for a pre-launch beta but must not throw.

- [ ] **Step 8: Do NOT apply yet.** Applying is a DB-destructive step; it runs in Task 4 after explicit user confirmation.

---

### Task 3: `AppDbContext` config — DbSet, both composite FKs, the alternate key

**Files:**
- Modify: `ProjectCeres/Data/AppDbContext.cs`

**Interfaces:**
- Consumes: `SupportMessage`, the modified `SupportTicket`/`SupportTicketAttachment` (Task 1).
- Produces: `DbSet<SupportMessage> SupportMessages`; the two composite FKs + the message AK, so `dotnet ef migrations add` (Task 2) scaffolds the right schema.

- [ ] **Step 1: Add the DbSet** near the existing `SupportTickets` / `SupportTicketAttachments` sets:

```csharp
public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
```

- [ ] **Step 2: Configure `SupportMessage` in `OnModelCreating`** (mirror the existing `SupportTicketAttachment` block ~186–225):

```csharp
modelBuilder.Entity<SupportMessage>(b =>
{
    b.HasKey(m => m.Id);
    b.HasAlternateKey(m => new { m.Id, m.UserId });   // principal key for attachment→message
    b.HasIndex(m => m.UserId);                          // RLS policy filters on UserId
    b.Property(m => m.Body).IsRequired();

    // Composite FK message → ticket, so an agent message can only ever hang off a ticket with
    // the SAME owner. Postgres runs FK checks + cascade through an RI trigger RLS does not touch,
    // so a single-column FK would allow a cross-tenant write. Mirrors TransactionAttachment.
    b.HasOne(m => m.SupportTicket)
        .WithMany(t => t.Messages)
        .HasForeignKey(m => new { m.SupportTicketId, m.UserId })
        .HasPrincipalKey(t => new { t.Id, t.UserId })
        .OnDelete(DeleteBehavior.Cascade);
});
```

- [ ] **Step 3: Re-point the `SupportTicketAttachment` FK from ticket to message** (replace the existing block):

```csharp
modelBuilder.Entity<SupportTicketAttachment>(b =>
{
    b.HasKey(a => a.Id);
    b.HasIndex(a => a.UserId);
    b.Property(a => a.FileName).HasMaxLength(255).IsRequired();
    b.Property(a => a.StoredPath).HasMaxLength(500).IsRequired();
    b.Property(a => a.ContentType).HasMaxLength(100).IsRequired();

    b.HasOne(a => a.SupportMessage)
        .WithMany(m => m.Attachments)
        .HasForeignKey(a => new { a.SupportMessageId, a.UserId })
        .HasPrincipalKey(m => new { m.Id, m.UserId })
        .OnDelete(DeleteBehavior.Cascade);
});
```

- [ ] **Step 4: Confirm `SupportTicket` keeps its `(Id, UserId)` alternate key** (it needs it as the principal key for the message→ticket FK). If the 12.5 config only added it for the old attachment FK, keep it.

- [ ] **Step 5: Build.** `dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -c "error CS"` → expect 0. Now Tasks 1+3 compile together. **Generate the Task 2 migration now** (Task 2 Step 1), hand-edit it (Task 2 Steps 2–7).

- [ ] **Step 6: Commit Tasks 1 + 2 + 3 together** (the model does not build without the DbContext config, and the migration belongs with them):

```bash
git add ProjectCeres/Models/SupportMessage.cs ProjectCeres/Models/SupportMessageAuthor.cs ProjectCeres/Models/SupportTicket.cs ProjectCeres/Models/SupportTicketStatus.cs ProjectCeres/Models/SupportTicketAttachment.cs ProjectCeres/Data/AppDbContext.cs ProjectCeres/Migrations/
git commit -m "feat(12.6): SupportMessage entity, both composite FKs, cutover migration"
```

---

### Task 4: Apply the migration (DB-destructive — explicit confirmation)

**Files:** none (DB operation)

- [ ] **Step 1: PAUSE and ask the user to confirm the migration may be applied to the dev database.** This is a DB-destructive step (drops `Message`, moves data). Do not proceed without an explicit yes. Show the user the migration's `Up()` first.

- [ ] **Step 2: On confirmation, apply.** `dotnet ef database update --project ProjectCeres`. Expect: success, no constraint violations (the pre-flight guard + owner-matched repoint guarantee clean data).

- [ ] **Step 3: Verify the shape.** `PGPASSWORD=... psql -h localhost -U ceres_migrator -d project_ceres -c '\d "SupportMessages"'` — confirm the table, the AK, both FKs. Confirm `SupportTickets` no longer has `Message` and has `ExternalRef`.

- [ ] **Step 4: Verify RLS parity.** `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~ParityTests"` → PASS (proves the `SupportMessages` policy landed and matches the model-derived set).

---

### Task 5: `SupportTicketStateMachine` (static class) + unit `[Theory]`

**Files:**
- Create: `ProjectCeres/Services/SupportTicketStateMachine.cs`, `ProjectCeres.Tests/Unit/SupportTicketStateMachineTests.cs`

**Interfaces:**
- Produces: `static class SupportTicketStateMachine { static TransitionResult ResolveUserReply(SupportTicketStatus current); static TransitionResult ResolveOperatorAction(SupportTicketStatus current, SupportTicketStatus chosen); }`; `readonly record struct TransitionResult(bool Allowed, SupportTicketStatus NewStatus, string? Reason)`.

- [ ] **Step 1: Write the failing unit tests** (`SupportTicketStateMachineTests.cs`), the whole transition table as a `[Theory]`:

```csharp
using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;
using Xunit;

namespace ProjectCeres.Tests.Unit;

public class SupportTicketStateMachineTests
{
    [Theory]
    [InlineData(SupportTicketStatus.Open, SupportTicketStatus.Open)]      // reply to Open stays Open
    [InlineData(SupportTicketStatus.Pending, SupportTicketStatus.Open)]   // ball returns to operator
    [InlineData(SupportTicketStatus.OnHold, SupportTicketStatus.Open)]
    [InlineData(SupportTicketStatus.Solved, SupportTicketStatus.Open)]    // the reopen
    public void UserReply_moves_status_to_Open(SupportTicketStatus from, SupportTicketStatus expected)
    {
        var r = SupportTicketStateMachine.ResolveUserReply(from);
        r.Allowed.Should().BeTrue();
        r.NewStatus.Should().Be(expected);
    }

    [Fact]
    public void UserReply_to_Closed_is_refused()
    {
        var r = SupportTicketStateMachine.ResolveUserReply(SupportTicketStatus.Closed);
        r.Allowed.Should().BeFalse();
        r.Reason.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(SupportTicketStatus.Open, SupportTicketStatus.Pending)]
    [InlineData(SupportTicketStatus.Open, SupportTicketStatus.OnHold)]
    [InlineData(SupportTicketStatus.Open, SupportTicketStatus.Solved)]
    [InlineData(SupportTicketStatus.Pending, SupportTicketStatus.Open)]
    [InlineData(SupportTicketStatus.OnHold, SupportTicketStatus.Solved)]
    [InlineData(SupportTicketStatus.Solved, SupportTicketStatus.Closed)]
    [InlineData(SupportTicketStatus.Open, SupportTicketStatus.Closed)]   // direct close (spam/dup)
    public void OperatorAction_sets_the_chosen_status(SupportTicketStatus from, SupportTicketStatus chosen)
    {
        var r = SupportTicketStateMachine.ResolveOperatorAction(from, chosen);
        r.Allowed.Should().BeTrue();
        r.NewStatus.Should().Be(chosen);
    }

    [Theory]
    [InlineData(SupportTicketStatus.Closed, SupportTicketStatus.Open)]
    [InlineData(SupportTicketStatus.Closed, SupportTicketStatus.Pending)]
    public void OperatorAction_cannot_leave_Closed(SupportTicketStatus from, SupportTicketStatus chosen)
    {
        var r = SupportTicketStateMachine.ResolveOperatorAction(from, chosen);
        r.Allowed.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run, verify fail.** `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~SupportTicketStateMachineTests"` → FAIL (type not found).

- [ ] **Step 3: Implement `SupportTicketStateMachine.cs`.**

```csharp
using ProjectCeres.Models;

namespace ProjectCeres.Services;

/// <summary>
/// The support-ticket status transitions, as data. Pure and static (like BudgetPeriod):
/// no DbContext, no request state, no fields. Rules live here; the DB writes live in the
/// services, so the rules exist in one place and are unit-testable with no database.
/// User actions derive status; operator actions carry an explicit chosen status.
/// </summary>
public static class SupportTicketStateMachine
{
    public static TransitionResult ResolveUserReply(SupportTicketStatus current) =>
        current == SupportTicketStatus.Closed
            ? new(false, current, "This ticket is closed. File a follow-up ticket to continue.")
            : new(true, SupportTicketStatus.Open, null);   // any open reply returns the ball to the operator

    public static TransitionResult ResolveOperatorAction(SupportTicketStatus current, SupportTicketStatus chosen) =>
        current == SupportTicketStatus.Closed
            ? new(false, current, "This ticket is closed and cannot change status.")
            : new(true, chosen, null);
}

public readonly record struct TransitionResult(bool Allowed, SupportTicketStatus NewStatus, string? Reason);
```

- [ ] **Step 4: Run, verify pass.** Same filter → PASS.

- [ ] **Step 5: Commit.**

```bash
git add ProjectCeres/Services/SupportTicketStateMachine.cs ProjectCeres.Tests/Unit/SupportTicketStateMachineTests.cs
git commit -m "feat(12.6): SupportTicketStateMachine — static transition rules + theory"
```

---

### Task 6: `SupportTicketService.CreateAsync` writes the first message; migrate/rewrite its tests

**Files:**
- Modify: `ProjectCeres/Services/SupportTicketService.cs`, `ISupportTicketService.cs`
- Modify (rewrite): `ProjectCeres.Tests/Integration/SupportTicketServiceTests.cs` (the 3 Create/attachment rewrites; attachment rewrites wait for Task 7)

**Interfaces:**
- Consumes: `SupportMessage`, `SupportMessageAuthor` (Task 1).
- Produces: `CreateAsync` now inserts a `SupportTicket` + one `SupportMessage(AuthorRole=User, Body=message, UserId=owner)` in one `SaveChanges`.

- [ ] **Step 1: Rewrite `CreateAsync`.** It currently sets `ticket.Message`; instead create the ticket AND a first `SupportMessage`. The `subject`/`message`/`priority`/`precedingTicketId` signature is unchanged; `message` becomes the first message's `Body`.

```csharp
var now = _timeProvider.GetUtcNow().UtcDateTime;
var ticket = new SupportTicket
{
    Id = Guid.NewGuid(), UserId = _user.UserId, Subject = subject.Trim(),
    Status = SupportTicketStatus.Open, Priority = priority,
    PrecedingTicketId = precedingTicketId, CreatedAt = now, UpdatedAt = now,
};
ticket.Messages.Add(new SupportMessage
{
    Id = Guid.NewGuid(), UserId = _user.UserId, SupportTicketId = ticket.Id,
    AuthorRole = SupportMessageAuthor.User, Body = message.Trim(), CreatedAt = now,
});
_db.SupportTickets.Add(ticket);
await _db.SaveChangesAsync(ct);
return ticket;
```

(Keep the existing blank-subject/message guards and the follow-up validation exactly as they are.)

- [ ] **Step 2: Rewrite the affected service tests** per the inventory: `CreateAsync_opens_the_ticket_and_stamps_the_current_user` asserts the first `SupportMessage` exists (`AuthorRole=User`, owner-stamped, Body = supplied text); `CreateAsync_links_a_follow_up_to_the_closed_ticket_it_continues` additionally asserts the first message. Filter every DB read by the unique subject marker already used.

- [ ] **Step 3: Run.** `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~SupportTicketServiceTests"` → the Create/follow-up tests PASS; the attachment tests still FAIL to compile (they reference the old upload method — fixed in Task 7). Expected; do not commit until Task 7.

---

### Task 7: `FileAttachmentService` — support upload targets a message; rewrite attachment tests

**Files:**
- Modify: `ProjectCeres/Services/IFileAttachmentService.cs`, `FileAttachmentService.cs`
- Modify (rewrite): `ProjectCeres.Tests/Integration/SupportTicketServiceTests.cs` (the 6 attachment rewrites)

**Interfaces:**
- Consumes: `SupportMessage`.
- Produces: `UploadForSupportMessageAsync(Guid supportMessageId, IFormFile) → SupportTicketAttachment`; `GetSupportTicketAttachmentAsync` owner-scope now walks message→ticket→owner; `DeleteSupportTicketAttachmentAsync` unchanged in signature.

- [ ] **Step 1: Rename `UploadForSupportTicketAsync` → `UploadForSupportMessageAsync`** on the interface and impl. Parent check becomes: the message must exist and be owned by the current user (`db.SupportMessages.Owned(user).AnyAsync(m => m.Id == supportMessageId)`). The per-parent count cap is now per-message (count `SupportTicketAttachments` where `SupportMessageId == id`). Path `uploads/support/{supportMessageId}/{guid}{ext}`. Set `attachment.SupportMessageId` + `attachment.UserId = user.UserId` explicitly.

- [ ] **Step 2: Update `GetSupportTicketAttachmentAsync`** owner-scope: `.Where(a => a.Id == attachmentId && a.SupportMessage.SupportTicket.UserId == user.UserId)`.

- [ ] **Step 3: Rewrite the 6 attachment tests** per the inventory — re-point to `UploadForSupportMessageAsync`, seeding a ticket + first message and attaching to the message. Keep the negative-assertion discipline (wrong-type, 10 MB cap, 10-per-message cap, quota, cross-tenant, cross-tenant read).

- [ ] **Step 4: Run.** `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~SupportTicketServiceTests"` → all PASS now.

- [ ] **Step 5: Commit Tasks 6 + 7.**

```bash
git add ProjectCeres/Services/SupportTicketService.cs ProjectCeres/Services/ISupportTicketService.cs ProjectCeres/Services/IFileAttachmentService.cs ProjectCeres/Services/FileAttachmentService.cs ProjectCeres.Tests/Integration/SupportTicketServiceTests.cs
git commit -m "feat(12.6): create writes the first message; attachments target a message"
```

---

### Task 8: `SupportMessageService` — the user reply, driven by the state machine

**Files:**
- Create: `ProjectCeres/Services/ISupportMessageService.cs`, `SupportMessageService.cs`, `ProjectCeres.Tests/Integration/SupportMessageServiceTests.cs`
- Modify: `ProjectCeres/Program.cs` (DI)

**Interfaces:**
- Consumes: `SupportTicketStateMachine`, `SupportMessage`.
- Produces: `ISupportMessageService { Task<SupportMessage> PostUserReplyAsync(Guid ticketId, string body, CancellationToken ct); Task<SupportTicket?> GetThreadAsync(Guid ticketId, CancellationToken ct); }`. `PostUserReplyAsync` throws `InvalidOperationException` (rejection reason) on a Closed ticket or an unreachable one.

- [ ] **Step 1: Write the failing tests** (`SupportMessageServiceTests.cs`), against the real DB via `TestDbFixture` (mirror `SupportTicketServiceTests` setup):

```csharp
[Fact] public async Task PostUserReplyAsync_appends_a_user_message_and_returns_status_to_Open() { /* create ticket (Open→operator sets Pending via admin path not available here, so seed a Pending ticket via admin ctx), post reply, assert new SupportMessage AuthorRole=User + ticket.Status==Open */ }
[Fact] public async Task PostUserReplyAsync_reopens_a_Solved_ticket() { /* seed Solved via admin ctx, reply, assert Open */ }
[Fact] public async Task PostUserReplyAsync_refuses_a_reply_to_a_Closed_ticket() { /* seed Closed, reply → throws */ }
[Fact] public async Task PostUserReplyAsync_cannot_reach_another_users_ticket() { /* foreign ticket via admin ctx, reply → throws (NotFound-shaped) */ }
[Fact] public async Task GetThreadAsync_returns_messages_in_order_including_agent_messages() { /* seed a user msg + an agent msg (owner-stamped) via admin ctx, assert both read back ordered */ }
```

Use the `CreateAdminContext()` pattern from `SupportTicketServiceTests` to seed foreign / agent / non-Open rows (the app-role fixture cannot forge them — that is RLS working).

- [ ] **Step 2: Run, verify fail.** Filter `~SupportMessageServiceTests` → FAIL (service not found).

- [ ] **Step 3: Implement `SupportMessageService`.** `PostUserReplyAsync`: load the ticket via `.Owned(_user)`; if null throw "not found"; `ResolveUserReply(ticket.Status)`, throw `result.Reason` if not allowed; add a `SupportMessage(AuthorRole=User, UserId=_user.UserId, Body=body.Trim())`; `ticket.Status = result.NewStatus`; `ticket.UpdatedAt = now`; `SaveChanges`. `GetThreadAsync`: `.Owned(_user).Include(t => t.Messages).ThenInclude(m => m.Attachments).FirstOrDefault`, messages ordered by `CreatedAt`.

- [ ] **Step 4: Register DI** in `Program.cs` near `ISupportTicketService`: `builder.Services.AddScoped<ISupportMessageService, SupportMessageService>();`

- [ ] **Step 5: Run, verify pass.** Filter → PASS.

- [ ] **Step 6: Commit.**

```bash
git add ProjectCeres/Services/ISupportMessageService.cs ProjectCeres/Services/SupportMessageService.cs ProjectCeres/Program.cs ProjectCeres.Tests/Integration/SupportMessageServiceTests.cs
git commit -m "feat(12.6): SupportMessageService — user reply through the state machine"
```

---

### Task 9: User API — thread GET, reply endpoint, list gains messageCount/lastMessageAt

**Files:**
- Modify: `ProjectCeres/Controllers/Api/SupportApiController.cs`
- Create: `ProjectCeres.Tests/Integration/Api/SupportConversationApiTests.cs`
- Modify (rewrite): `ProjectCeres.Tests/Integration/Api/SupportApiTests.cs` (List + thread rewrites)

**Interfaces:**
- Consumes: `ISupportMessageService`.
- Produces: `GET /api/support/tickets/{id}` returns the ordered thread (messages + per-message attachments); `POST /api/support/tickets/{id}/messages` (user reply; `422` on Closed via the service throw → the existing `Validation(ex)` envelope); `GET /api/support/tickets` DTO gains `messageCount`, `lastMessageAt`.

- [ ] **Step 1: Write failing API tests** (`SupportConversationApiTests.cs`): reply round-trip (post → appears in thread → status flips to Open); reply-to-Closed → 422; reply-to-another-users-ticket → 404/422 (IDOR); thread GET returns messages in order including an admin-seeded agent message; list returns `messageCount`/`lastMessageAt`. Rate-limit attribute presence is asserted in Task 12's architecture test, not here.

- [ ] **Step 2: Run, verify fail.**

- [ ] **Step 3: Implement.** Add the reply action (`[HttpPost("tickets/{ticketId:guid}/messages")]`, `[ApplyEmailIpRateLimit]`, `[EnableRateLimiting(AuthRateLimitPolicies.EmailByUser)]` — it notifies the operator); call `SupportMessageService.PostUserReplyAsync`, wrap the throw in `Validation(ex)`; on success call the notification service (Task 11) then return the created message DTO. Change `GetOne` to return the full thread via `GetThreadAsync`. Extend the list DTO + `ListOwnAsync` projection with `messageCount = t.Messages.Count` and `lastMessageAt = t.Messages.Max(m => m.CreatedAt)`.

- [ ] **Step 4: Rewrite the affected `SupportApiTests` per the inventory** (List gains the new fields; the create-returns-201 test adds the first-message assertion).

- [ ] **Step 5: Run, verify pass.** Filter `~SupportConversationApiTests|~SupportApiTests` → PASS. (Notification wiring stubbed until Task 11; if Task 11 is not yet done, the notify call is a no-op guarded by the resolver returning null — order Task 11 before Step 3's notify call, or land the notify call in Task 11.)

- [ ] **Step 6: Commit** (defer to Task 11 so the notify call is real).

---

### Task 10: Operator API — the `[RequireAdmin]` reply/status endpoint under `Admin/`

**Files:**
- Create: `ProjectCeres/Admin/SupportAdminApiController.cs`, `ProjectCeres.Tests/Integration/Admin/SupportAdminApiTests.cs`
- Modify: `ProjectCeres/Models/AuditLog.cs` (+`SupportMessageByAgent`), `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs` (documented-set)

**Interfaces:**
- Consumes: `AdminDbContext`, `SupportTicketStateMachine`, `IAuditLogWriter`.
- Produces: `POST /api/admin/support/tickets/{id}/messages` — operator reply and/or status set.

- [ ] **Step 1: Add `AuditLogAction.SupportMessageByAgent`** to the enum AND to `ArchitectureTests.AuditLogAction_enum_values_match_documented_set`'s `expected` array (same commit or the test goes red).

- [ ] **Step 2: Write failing operator tests** (`SupportAdminApiTests.cs`, under `Integration/Admin/`, using the admin-authenticated factory pattern from `AdminUsersApiController` tests):
  - operator reply writes a `SupportMessage(AuthorRole=Agent)` stamped with the **ticket owner's** UserId (not the admin's), sets the chosen status, writes an `AuditLog(SupportMessageByAgent, EntityId=ticketId)`.
  - status-only change (empty body + a status) creates NO message row, only the transition.
  - reply to a nonexistent ticket → 404.
  - a non-admin caller → 403 (the `[RequireAdmin]` gate).
  - an illegal transition (status on a Closed ticket) → 422.

- [ ] **Step 3: Run, verify fail.**

- [ ] **Step 4: Implement `SupportAdminApiController`** under the `ProjectCeres.Admin` namespace, `[Route("api/admin/support")]`, `[RequireAdmin]`. Inject `AdminDbContext`, `SupportTicketStateMachine` (static — call directly), `IAuditLogWriter`, `ICurrentUserAccessor`. The action:
  1. Load the ticket: `_adminDb.SupportTickets.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == id)`; **404 if null**.
  2. `ResolveOperatorAction(ticket.Status, request.Status)`; if not allowed → 422 `Validation`.
  3. If `request.Body` is non-empty: add `new SupportMessage { UserId = ticket.UserId /* NOT the admin */, SupportTicketId = ticket.Id, AuthorRole = Agent, Body = request.Body.Trim(), CreatedAt = now }`. Empty body → no message row.
  4. `ticket.Status = result.NewStatus; ticket.UpdatedAt = now;`
  5. `await _adminDb.SaveChangesAsync();`
  6. `await _auditLog.RecordAsync(ticket.UserId, AuditLogAction.SupportMessageByAgent, nameof(SupportTicket), ticket.Id, ct);`
  7. If a reply was posted, call the notification service to email the user (Task 11).

  **Comment the explicit `UserId = ticket.UserId` line** noting the interceptor would otherwise stamp the admin.

- [ ] **Step 5: Run, verify pass.**

- [ ] **Step 6: Commit** (defer notify to Task 11).

---

### Task 11: `SupportNotificationService` — extract, add the two user-facing templates

**Files:**
- Create: `ProjectCeres/Common/Email/SupportNotificationService.cs`
- Modify: `ProjectCeres/Common/Email/EmailTemplateKey.cs`, `EmailComposer.cs`, `Resources/EmailsResource.en.resx`, `.es.resx`, `SupportApiController.cs` (use the service), `SupportAdminApiController.cs` (use the service), `ArchitectureTests.cs` (recipient-lock allow-list + `mailSendingActions`), `EmailComposerTests.cs` (resx key count)
- Modify: `ProjectCeres/Common/Email/EmailRecipient.cs` — nothing (FromVerifiedUser exists); the whole-tree test's allow-list gains `SupportNotificationService.cs`
- Create: notification tests in `SupportConversationApiTests` / `SupportAdminApiTests` (capture the email)

**Interfaces:**
- Produces: `SupportNotificationService` with methods `NotifyOperatorOfNewTicketAsync`, `NotifyOperatorOfUserReplyAsync`, `NotifyUserOfAgentReplyAsync(ticket, agentBody)`, `NotifyUserSolvedAsync(ticket)`. Two new `EmailTemplateKey`: `SupportReplyToUser`, `SupportTicketSolved`.

- [ ] **Step 1: Add the two `EmailTemplateKey` values** + the EN/ES resx triples (`SupportReplyToUser.Subject/BodyText/BodyHtml`, `SupportTicketSolved.Subject/BodyText/BodyHtml`) + the `EmailComposer` switch arms. `SupportReplyToUser` includes the agent body as an arg AND a `/support/<id>` link. Bump `EmailComposerTests` key count from 42 to 48 (2 templates × 3 keys × … actually +6 keys → 48).

- [ ] **Step 2: Write the failing tests** first: `NotifyUserOfAgentReplyAsync` sends via `FromVerifiedUser` to the owner's address, subject/body include the reply text; **a body containing HTML/template-breaking chars is escaped** (the H2 sanitisation negative test — assert the rendered `BodyHtml` has the raw `<script>` escaped).

- [ ] **Step 3: Run, verify fail.**

- [ ] **Step 4: Implement `SupportNotificationService`** — move `NotifySupportAsync` out of `SupportApiController` into this service, add the two user-facing methods using `EmailRecipient.FromVerifiedUser` (resolve the owner's `ApplicationUser.Email` server-side). The agent body flows through the composer's HTML-encoded arg path (the sanitisation the test pins). Register DI.

- [ ] **Step 5: Wire the call sites** — `SupportApiController` (create → operator, user-reply → operator), `SupportAdminApiController` (agent reply → user, Solved → user). Add `SupportNotificationService.cs` to the recipient-lock whole-tree allow-list in `EmailRecipientTests`, and add the two new mail-sending actions to `ArchitectureTests.mailSendingActions`.

- [ ] **Step 6: Run, verify pass** across `~Support` + `~EmailRecipientTests` + `~EmailComposerTests` + `~Email_triggering_endpoints`.

- [ ] **Step 7: Commit Tasks 9 + 10 + 11 together** (the API endpoints and their notifications are one coherent surface):

```bash
git add ProjectCeres/Controllers/Api/SupportApiController.cs ProjectCeres/Admin/SupportAdminApiController.cs ProjectCeres/Common/Email/ ProjectCeres/Models/AuditLog.cs ProjectCeres/Resources/ ProjectCeres/Program.cs ProjectCeres.Tests/
git commit -m "feat(12.6): user reply + operator reply endpoints + bidirectional notifications"
```

---

### Task 12: Architecture-test reconciliation sweep

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs`

Consolidation checkpoint — verify the three architecture-test touch-points landed (some in earlier tasks; this task confirms none was missed):

- [ ] **Step 1: `AuditLogAction_enum_values_match_documented_set`** includes `SupportMessageByAgent` (Task 10).
- [ ] **Step 2: `Email_triggering_endpoints_carry_a_rate_limit`'s `mailSendingActions`** includes `(SupportApiController, "PostMessage")` and `(SupportAdminApiController, "PostMessage")` (or the actual action names). Verify load-bearing: comment out one attribute, watch it fail, restore.
- [ ] **Step 3: The recipient-lock whole-tree test** allow-list includes `SupportNotificationService.cs` (Task 11). Verify load-bearing by planting a stray `FromVerifiedUser` call.
- [ ] **Step 4: Run** `dotnet test --filter "FullyQualifiedName~ArchitectureTests"` → PASS. Commit if any edit was needed here.

---

### Task 13: The `/support` SPA page (via frontend-orchestrator)

**Files:**
- Modify: `ProjectCeres.Client/src/app/pages/Support.tsx`
- Create: `ProjectCeres.Client/src/app/features/support/*` (page, api client, sheet, composer, tests)

**This task routes through the `frontend-orchestrator` skill at execution time (CLAUDE.md rule).** It is Phase 1 (new surface) — the shape brief was effectively settled in brainstorm (slide-in sheet, URL-reflected, status badges), so the orchestrator resumes into build.

- [ ] **Step 1: Enter via `frontend-orchestrator`.** State: new surface, thread in a URL-reflected slide-in sheet (`/support/<id>`), composer, follow-up affordance on Closed, list gains messageCount/lastMessageAt.
- [ ] **Step 2: Status badge mapping** (variants already exist in `badge.tsx`): Open=`info`, Pending=`warning`, OnHold=`secondary`, Solved=`success`, Closed=`outline`. No new tokens.
- [ ] **Step 3: Build** — `support-api.ts` (list, thread GET, create, reply, close), the page (list + `DataTransition` empty/loading/error, mirror `SessionsPage`), the sheet (opens from `/support/<id>` on load, closes on back), the composer (hidden on Closed; shows the follow-up affordance instead).
- [ ] **Step 4: Vitest** — list renders; sheet opens from a URL; composer posts and the thread re-renders; status badges render the right variant; Closed hides the composer and shows the follow-up affordance; empty/loading/error states.
- [ ] **Step 5: Run** `pnpm --dir ProjectCeres.Client test` + `pnpm --dir ProjectCeres.Client build` → PASS.
- [ ] **Step 6: Show the rendered result and wait for explicit approval before committing** (CLAUDE.md rule — silence ≠ approval).
- [ ] **Step 7: Commit on approval.**

---

### Task 14: E2E (Playwright)

**Files:**
- Create: an E2E spec under the project's Playwright suite (mirror the Stage 9.11 foundation).

- [ ] **Step 1: Write the E2E flows** — file a ticket → list → open in the sheet → first message reads back; deep-link straight to `/support/<id>` opens the sheet on load; user reply round-trip with the badge updating; Solved shows a composer (reopen works), Closed hides it and shows the follow-up affordance; 375px mobile (sheet full-width, ≥44px targets).
- [ ] **Step 2: The agent-reply leg is API-seeded** — post an agent message through the admin endpoint (there is no operator UI this stage), then assert the user's browser renders it. Comment this in the spec so the seam is explicit.
- [ ] **Step 3: Run the E2E suite** per the project's runner; PASS.
- [ ] **Step 4: Commit.**

---

### Task 15: Docs sync + roadmap tick + stage close

**Files:**
- Modify: `docs/models.md`, `docs/api-contract.md`, `docs/security-model.md`, `docs/roadmap-phase-three.md`

- [ ] **Step 1: Run the `sync-docs` skill** against the full 12.6 diff — it routes the changes to the right doc files (models.md SupportTicket/SupportMessage; api-contract.md message endpoints; security-model.md recipient-lock factory table gains no *new* factory but the notification-service call site is worth a line).
- [ ] **Step 2: Run the `changelog-sync` skill** if a CHANGELOG is present.
- [ ] **Step 3: Tick the Stage 12.6 roadmap items** (create the Stage 12.6 section if not present; the two Stage-13 receiving `[ ]` items already exist). Mark automated-test-covered items `[x]`, leave manual browser items `[ ]`.
- [ ] **Step 4: Final full verification** — `dotnet build`, `dotnet test` (background, once), `pnpm --dir ProjectCeres.Client build`, `pnpm --dir ProjectCeres.Client test` all exit 0.
- [ ] **Step 5: Commit the close-out.**

---

## Self-review

**Spec coverage:** Data model (Tasks 1, 3) ✓; state machine (Task 5) ✓; user reply + thread (Tasks 8, 9) ✓; operator endpoint with the interceptor trap + AdminDbContext + explicit stamp (Task 10) ✓; notifications incl. sanitisation negative test (Task 11) ✓; migration one-transaction + CASE remap + confirmation gate (Tasks 2, 4) ✓; both composite FKs + AK + negative test (Tasks 3, and the cross-tenant negative test belongs in Task 8/10 — **added note**: the real-DB cross-tenant negative test the spec mandates is written in Task 10 Step 2 via the admin-context seed proving a foreign-owner message cannot be written); ExternalRef column (Task 1) ✓; frontend (Task 13) ✓; four test layers (Tasks 5 unit, 8/9/10 integration, 13 Vitest, 14 E2E) ✓; audit via interim AuditLog (Task 10) ✓; rate-limit array (Tasks 9, 12) ✓; test reconciliation (Task 0) ✓.

**Placeholder scan:** the frontend task (13) delegates to frontend-orchestrator rather than inlining TSX — that is a deliberate routing requirement, not a placeholder. All backend steps carry real code.

**Type consistency:** `UploadForSupportMessageAsync` used consistently (Tasks 7, 9); `SupportMessageAuthor.User/Agent` (Tasks 1, 6, 8, 10); `TransitionResult` fields (Task 5) consumed by name in Tasks 8, 10; `AuditLogAction.SupportMessageByAgent` (Tasks 10, 12).

**One gap found + fixed inline:** the spec's real-DB composite-FK cross-tenant negative test needed an explicit home — assigned to Task 10 Step 2 (foreign-owner message insert rejected) and Task 8 (foreign ticket unreachable). The message→ticket composite FK's structural guarantee is proven there.
