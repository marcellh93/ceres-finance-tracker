# Stage 9.5c — Roslyn analyzers + EN/ES resx parity source generator — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship 4 Roslyn analyzers (CER001/CER002/CER004/CER010) + 1 source generator (CER020) + 3 escape attributes; retro-decorate ~85 existing call sites; reach `error`-severity enforcement after a 48h soak with the baseline drained to zero.

**Architecture:** Three new csproj projects under `ProjectCeres.sln` — `ProjectCeres.Analyzers.Annotations` (netstandard2.0, attribute definitions), `ProjectCeres.Analyzers` (netstandard2.0, diagnostic + source-gen classes), `ProjectCeres.Analyzers.Tests` (net10.0, xUnit + `Microsoft.CodeAnalysis.Testing`). The analyzers are wired into `ProjectCeres.csproj` via `<ProjectReference OutputItemType="Analyzer">`. Repo-root `Directory.Build.props` + `.editorconfig` carry shared analyzer config + per-analyzer baseline-suppression sections.

**Tech Stack:** .NET 10 (consumers) + netstandard2.0 (analyzers, mandatory per Roslyn SDK), `Microsoft.CodeAnalysis.CSharp` (analyzer + generator framework), `Microsoft.CodeAnalysis.Testing` (Verifier<T> testing pattern), xUnit, EditorConfig.

---

## Spec dependencies (binding throughout this plan)

- **Spec:** `docs/superpowers/specs/2026-05-26-stage-9-5c-roslyn-analyzers-design.md`
- **Locked decisions L1–L10:** see spec §2
- **Inherited conditions C-1 / C-2 / C-3:** see spec §3 (escape attrs ship before analyzers turn on; 48h soak AND drained baseline before warning→error flip; single repo-root `.editorconfig` for baseline-suppression)
- **Pre-flight findings F1 / F2 / F3:** see spec §4 (Directory.Build.props + .editorconfig don't exist today; ProjectReference shape follows EF Design precedent at `ProjectCeres.csproj:13-16`; BuildTailwind MSBuild target must keep firing)

## Commit chain at a glance

| Commit | Subagent-dispatchable? | Description |
|---|---|---|
| **N** | sequential | Annotations csproj + 3 attributes + Directory.Build.props + sln wiring |
| **N+1a** | parallel-with-1b | Retro-decorate ~7 services with `[PreAuthScope]` |
| **N+1b** | parallel-with-1a | Verify CER002 baseline = 0 against AppDbContext (no retro-decoration needed) |
| **N+1c** | n/a | (intentionally empty — CER003 was withdrawn during planning) |
| **N+1d** | sequential | TimeProvider DI registration + retro-decorate ~85 CER004 sites |
| **N+2** | sequential | Analyzers csproj + Tests csproj + ProjectReference wiring + .editorconfig baseline |
| **N+3 … N+M** | parallel batches | Baseline drain — migrate suppressed CER004 sites to TimeProvider in small groups |
| **N+M+1** | sequential | Flip warning→error + tick 9.5h's 9.5c row + cross-ref 9.1.6 tripwire |

---

## File structure (decomposition lock)

### New files

```
.editorconfig                                                                       (NEW, root)
Directory.Build.props                                                               (NEW, root)
ProjectCeres.Analyzers.Annotations/
  ProjectCeres.Analyzers.Annotations.csproj                                         (NEW)
  PreAuthScopeAttribute.cs                                                          (NEW)
  RlsBypassJustifiedAttribute.cs                                                    (NEW)
  AllowsWallClockAttribute.cs                                                       (NEW)
  RequiresAdminContextAttribute.cs                                                  (NEW — reserved for 9.5b)
ProjectCeres.Analyzers/
  ProjectCeres.Analyzers.csproj                                                     (NEW)
  PreAuthScopeTransactionAnalyzer.cs                                                (NEW, CER001)
  IgnoreQueryFiltersOnUserOwnedAnalyzer.cs                                          (NEW, CER002)
  DateTimeWallClockAnalyzer.cs                                                      (NEW, CER004)
  RlsBypassJustifiedTicketFormatAnalyzer.cs                                         (NEW, CER010)
  ResxParityGenerator.cs                                                            (NEW, CER020)
  Diagnostics.cs                                                                    (NEW, descriptor registry)
ProjectCeres.Analyzers.Tests/
  ProjectCeres.Analyzers.Tests.csproj                                               (NEW)
  CER001_PreAuthScopeTransactionAnalyzerTests.cs                                    (NEW)
  CER002_IgnoreQueryFiltersOnUserOwnedAnalyzerTests.cs                              (NEW)
  CER004_DateTimeWallClockAnalyzerTests.cs                                          (NEW)
  CER010_RlsBypassJustifiedTicketFormatAnalyzerTests.cs                             (NEW)
  CER020_ResxParityGeneratorTests.cs                                                (NEW)
  TestHelpers/CSharpAnalyzerVerifier.cs                                             (NEW, generic Verifier<T> wrapper)
```

### Modified files

```
ProjectCeres.sln                                  (3 new project entries)
ProjectCeres/ProjectCeres.csproj                  (2 new ProjectReferences)
ProjectCeres/Common/Authentication/AuditLogWriter.cs                  (add [PreAuthScope] on class)
ProjectCeres/Common/Authentication/EmailConfirmationService.cs        (add [PreAuthScope])
ProjectCeres/Common/Authentication/LockoutUnlockService.cs            (add [PreAuthScope])
ProjectCeres/Common/Authentication/MfaBackupCodeService.cs            (add [PreAuthScope])
ProjectCeres/Common/Authentication/PasswordResetService.cs            (add [PreAuthScope])
ProjectCeres/Common/Authentication/TotpReplayGuard.cs                 (add [PreAuthScope])
ProjectCeres/Controllers/Api/AuthController.cs                        (add [PreAuthScope])
ProjectCeres/Program.cs                                               (register TimeProvider in DI)
~25 files under ProjectCeres/Common/Authentication/, ProjectCeres/Services/, ProjectCeres/Controllers/Api/, ProjectCeres/ViewModels/, ProjectCeres/Models/ApplicationUser.cs   (CER004 retro-decoration: TimeProvider injection or [AllowsWallClock])
docs/roadmap-phase-three.md                       (tick Stage 9.5h sub-stage 9.5c at N+M+1; cross-ref 9.1.6 tripwire)
```

---

## Task 1: Commit N — Annotations csproj + 3 attribute definitions + Directory.Build.props

**Goal:** Ship the escape-attribute definitions BEFORE any analyzer runs (condition C-1). Repo builds clean after this commit; no analyzer is loaded yet.

**Files:**
- Create: `Directory.Build.props`
- Create: `ProjectCeres.Analyzers.Annotations/ProjectCeres.Analyzers.Annotations.csproj`
- Create: `ProjectCeres.Analyzers.Annotations/PreAuthScopeAttribute.cs`
- Create: `ProjectCeres.Analyzers.Annotations/RlsBypassJustifiedAttribute.cs`
- Create: `ProjectCeres.Analyzers.Annotations/AllowsWallClockAttribute.cs`
- Create: `ProjectCeres.Analyzers.Annotations/RequiresAdminContextAttribute.cs`
- Modify: `ProjectCeres.sln` (add 1 project entry)
- Modify: `ProjectCeres/ProjectCeres.csproj` (add ProjectReference to Annotations)

**Subagent-dispatchable?** Sequential (everything downstream depends on this commit landing).

### Steps

- [ ] **Step 1: Create `Directory.Build.props` at repo root**

Write `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Verify Directory.Build.props doesn't break existing build**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: `Build succeeded` (no new errors). If new IDE0001 / IDE0058 warnings appear, that's expected — those are the warnings 9.1.6 will eventually address; they don't fail the build.

- [ ] **Step 3: Create `ProjectCeres.Analyzers.Annotations/ProjectCeres.Analyzers.Annotations.csproj`**

Write:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <IsPackable>false</IsPackable>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
  </PropertyGroup>

</Project>
```

Rationale: `netstandard2.0` (per spec L9 / pitfall #1 from research) so the assembly is loadable by the .NET 10 consumer AND by the analyzer host. `Nullable enable` matches project convention. `LangVersion latest` so we get C# 12 features in the attribute definitions if needed.

- [ ] **Step 4: Create the 4 attribute files**

`ProjectCeres.Analyzers.Annotations/PreAuthScopeAttribute.cs`:

```csharp
namespace ProjectCeres.Analyzers.Annotations;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PreAuthScopeAttribute : Attribute
{
}
```

`ProjectCeres.Analyzers.Annotations/RlsBypassJustifiedAttribute.cs`:

```csharp
namespace ProjectCeres.Analyzers.Annotations;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RlsBypassJustifiedAttribute : Attribute
{
    public RlsBypassJustifiedAttribute(string ticket) => Ticket = ticket;
    public string Ticket { get; }
}
```

`ProjectCeres.Analyzers.Annotations/AllowsWallClockAttribute.cs`:

```csharp
namespace ProjectCeres.Analyzers.Annotations;

[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor,
    AllowMultiple = false,
    Inherited = false)]
public sealed class AllowsWallClockAttribute : Attribute
{
    public AllowsWallClockAttribute(string reason) => Reason = reason;
    public string Reason { get; }
}
```

`ProjectCeres.Analyzers.Annotations/RequiresAdminContextAttribute.cs`:

```csharp
namespace ProjectCeres.Analyzers.Annotations;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RequiresAdminContextAttribute : Attribute
{
}
```

- [ ] **Step 5: Build the Annotations project in isolation**

Run: `dotnet build ProjectCeres.Analyzers.Annotations/ProjectCeres.Analyzers.Annotations.csproj`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s)`. Output assembly at `ProjectCeres.Analyzers.Annotations/bin/Debug/netstandard2.0/ProjectCeres.Analyzers.Annotations.dll`.

- [ ] **Step 6: Add the project to ProjectCeres.sln**

Run: `dotnet sln ProjectCeres.sln add ProjectCeres.Analyzers.Annotations/ProjectCeres.Analyzers.Annotations.csproj`
Expected: `Project ProjectCeres.Analyzers.Annotations/ProjectCeres.Analyzers.Annotations.csproj added to the solution.`

- [ ] **Step 7: Add ProjectReference from ProjectCeres to Annotations**

In `ProjectCeres/ProjectCeres.csproj`, add a new `<ItemGroup>` (or extend the existing one) BEFORE the `<Target Name="BuildTailwind">` line:

```xml
  <ItemGroup>
    <ProjectReference Include="..\ProjectCeres.Analyzers.Annotations\ProjectCeres.Analyzers.Annotations.csproj" />
  </ItemGroup>
```

This is a NORMAL reference (analyzer references come in Task 12, commit N+2). The Annotations DLL needs to be in `ProjectCeres`'s build output so the retro-decoration commits (N+1a / N+1d) can reference the attributes.

- [ ] **Step 8: Build ProjectCeres to verify the reference works**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: `Build succeeded.` In the output, expect to see `ProjectCeres.Analyzers.Annotations -> .../ProjectCeres.Analyzers.Annotations.dll` followed by `ProjectCeres -> .../ProjectCeres.dll`. F3 check: the `BuildTailwind` target still fires (look for `pnpm run build:css` exec output).

- [ ] **Step 9: Run the full test suite to confirm no regressions**

Run: `dotnet test`
Expected: existing 1098+ tests pass; no new failures. If anything fails, the cause is something the Annotations reference shouldn't have touched — investigate before proceeding.

- [ ] **Step 10: Commit**

```bash
git add Directory.Build.props \
        ProjectCeres.Analyzers.Annotations/ \
        ProjectCeres.sln \
        ProjectCeres/ProjectCeres.csproj
git commit -m "feat(9.5c): ship escape-attribute definitions before analyzers turn on

Stage 9.5c — Commit N. Per condition C-1 (escape attrs ship before
analyzers), this commit lands [PreAuthScope], [RlsBypassJustified],
[AllowsWallClock] (+ [RequiresAdminContext] reserved for 9.5b) in
ProjectCeres.Analyzers.Annotations csproj.

No analyzer runs yet — that's commit N+2. This commit makes the
attributes referenceable so the retro-decoration sweeps (N+1a / N+1d)
can land cleanly.

Directory.Build.props introduced at repo root (partial realisation of
Stage 9.1.6 tripwire bullet — cross-ref will be added at N+M+1).

Refs: docs/superpowers/specs/2026-05-26-stage-9-5c-roslyn-analyzers-design.md §3 C-1, §5.2, §10"
```

---

## Task 2: Commit N+1a — Retro-decorate ~7 services with `[PreAuthScope]`

**Goal:** Mark every class that calls `BeginPreAuthUserScopeAsync` with `[PreAuthScope]` so CER001 (commit N+2) has the marker target it needs.

**Files:**
- Modify: `ProjectCeres/Common/Authentication/AuditLogWriter.cs`
- Modify: `ProjectCeres/Common/Authentication/EmailConfirmationService.cs`
- Modify: `ProjectCeres/Common/Authentication/LockoutUnlockService.cs`
- Modify: `ProjectCeres/Common/Authentication/MfaBackupCodeService.cs`
- Modify: `ProjectCeres/Common/Authentication/PasswordResetService.cs`
- Modify: `ProjectCeres/Common/Authentication/TotpReplayGuard.cs`
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs`

**Subagent-dispatchable?** Parallel with N+1b (independent files, no shared state). One subagent for the 6 services in `Common/Authentication/`, one subagent for `AuthController.cs`, OR one subagent for all 7.

### Steps

- [ ] **Step 1: Confirm the exact list of classes**

Run: `grep -l 'BeginPreAuthUserScopeAsync' ProjectCeres/ -r --include='*.cs' | grep -v 'PreAuthRlsScope.cs' | grep -v '/Migrations/'`

Expected output (exactly 7 files):
```
ProjectCeres/Common/Authentication/AuditLogWriter.cs
ProjectCeres/Common/Authentication/EmailConfirmationService.cs
ProjectCeres/Common/Authentication/LockoutUnlockService.cs
ProjectCeres/Common/Authentication/MfaBackupCodeService.cs
ProjectCeres/Common/Authentication/PasswordResetService.cs
ProjectCeres/Common/Authentication/TotpReplayGuard.cs
ProjectCeres/Controllers/Api/AuthController.cs
```

If the count differs from 7, STOP and investigate — a new caller has appeared since the planning audit (2026-05-26) and may need different handling.

- [ ] **Step 2: For each file, locate the class declaration line**

Run: `grep -n 'public.*class\|public sealed class' ProjectCeres/Common/Authentication/AuditLogWriter.cs ProjectCeres/Common/Authentication/EmailConfirmationService.cs ProjectCeres/Common/Authentication/LockoutUnlockService.cs ProjectCeres/Common/Authentication/MfaBackupCodeService.cs ProjectCeres/Common/Authentication/PasswordResetService.cs ProjectCeres/Common/Authentication/TotpReplayGuard.cs ProjectCeres/Controllers/Api/AuthController.cs`

For each file, identify the line that starts with `public ... class <name>` and add `[PreAuthScope]` on the line directly above.

- [ ] **Step 3: Add `using` import + `[PreAuthScope]` attribute to each of the 7 files**

For EACH of the 7 files, perform two edits:

**Edit A — Add the using statement.** If the file doesn't already import `ProjectCeres.Analyzers.Annotations`, add it to the top with the other `using` lines:

```csharp
using ProjectCeres.Analyzers.Annotations;
```

**Edit B — Add the attribute.** On the line directly above `public sealed class <Name>` (or `public class <Name>`), add:

```csharp
[PreAuthScope]
```

Example for `AuditLogWriter.cs` (assuming line 12 is `public sealed class AuditLogWriter : IAuditLogWriter`):

Before:
```csharp
namespace ProjectCeres.Common.Authentication;

public sealed class AuditLogWriter : IAuditLogWriter
```

After:
```csharp
namespace ProjectCeres.Common.Authentication;

[PreAuthScope]
public sealed class AuditLogWriter : IAuditLogWriter
```

Same pattern for the other 6 files.

- [ ] **Step 4: Build to verify all 7 files compile**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: `Build succeeded. 0 Error(s)`. (Warnings unchanged from N.)

If a file errors with "type or namespace 'PreAuthScope' could not be found", the using statement was missed for that file.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test`
Expected: existing tests pass. No behavioural change — attributes are inert without an analyzer reading them.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Common/Authentication/AuditLogWriter.cs \
        ProjectCeres/Common/Authentication/EmailConfirmationService.cs \
        ProjectCeres/Common/Authentication/LockoutUnlockService.cs \
        ProjectCeres/Common/Authentication/MfaBackupCodeService.cs \
        ProjectCeres/Common/Authentication/PasswordResetService.cs \
        ProjectCeres/Common/Authentication/TotpReplayGuard.cs \
        ProjectCeres/Controllers/Api/AuthController.cs
git commit -m "feat(9.5c): mark 7 pre-auth services with [PreAuthScope]

Stage 9.5c — Commit N+1a. Retro-decorate the 7 services that call
BeginPreAuthUserScopeAsync with the [PreAuthScope] marker so CER001
(commit N+2) catches future drift.

Services: AuditLogWriter, EmailConfirmationService, LockoutUnlockService,
MfaBackupCodeService, PasswordResetService, TotpReplayGuard,
AuthController.

No behaviour change — marker is inert until CER001 ships at N+2.

Refs: docs/superpowers/specs/2026-05-26-stage-9-5c-roslyn-analyzers-design.md §6.1, §10"
```

---

## Task 3: Commit N+1b — Verify CER002 baseline is 0 (no retro-decoration needed)

**Goal:** Confirm — with a documented audit query — that no production code today does `IgnoreQueryFilters()` on an IUserOwned-entity query via `AppDbContext`. If the audit holds, this commit is a doc-only entry. If it doesn't hold, surface the violations and decorate them.

**Files:**
- (Conditional on audit) Modify: none expected (audit baseline = 0)
- (Conditional) Create: `docs/audits/2026-05-26-cer002-baseline-audit.md` (if any audit findings need recording)

**Subagent-dispatchable?** Parallel with N+1a (independent — different files, different concerns).

### Steps

- [ ] **Step 1: Run the audit query**

Run:
```bash
grep -rn 'IgnoreQueryFilters' ProjectCeres/ --include='*.cs' \
  | grep -v '/bin/\|/obj/\|/Migrations/\|//.*IgnoreQueryFilters' \
  | grep -v 'AdminDbContext\b\|adminDb\b\|_adminDb\b'
```

Expected: any remaining hits are candidates for CER002 violation. For each hit:
- Read the surrounding ~10 lines of context.
- Determine: is the receiver `AppDbContext` (CER002 fires)? Or `AdminDbContext` (CER002 excluded)? Or something else (e.g. a DbSet field directly — needs trace).

- [ ] **Step 2: Inspect each candidate hit's DbContext type**

For each line returned by Step 1, open the file and check:
- The class field declarations (look for `AdminDbContext`, `AppDbContext`, or a `DbContext` base type).
- The constructor injection (which DbContext is passed in).

Specifically check `ProjectCeres/Common/UserJobRunner.cs` — the planning audit found it uses `AdminDbContext db` (line 9), so its `IgnoreQueryFilters()` at line 24 is NOT a CER002 violation. Confirm this still holds.

- [ ] **Step 3a: If baseline = 0 — proceed to commit**

Skip to Step 4.

- [ ] **Step 3b: If baseline > 0 — decorate each site**

For each violation:
1. Add `using ProjectCeres.Analyzers.Annotations;` to the file if missing.
2. Add `[RlsBypassJustified("CER-NNNN")]` to the method that contains the `IgnoreQueryFilters()` call. The ticket string MUST match `^(CER|TICKET|ADR)-\d+$` — use `CER-9-5C-NNN` style or open a real ticket and cite it.
3. The justification ticket should be cross-referenced in a one-line comment if the reason isn't obvious from the ticket title.

Example:
```csharp
using ProjectCeres.Analyzers.Annotations;

// ...

[RlsBypassJustified("CER-9-5C-001")]
public async Task<SomeResult> LookupByIndexedField(string field, CancellationToken ct)
{
    var result = await _db.UserOwnedThings
        .IgnoreQueryFilters()
        .Where(/* ... */)
        .FirstOrDefaultAsync(ct);
}
```

- [ ] **Step 4: Build + test**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj && dotnet test`
Expected: green.

- [ ] **Step 5: Commit**

If baseline was 0:

```bash
git commit --allow-empty -m "feat(9.5c): confirm CER002 baseline = 0 (no AppDbContext + IUserOwned + IgnoreQueryFilters today)

Stage 9.5c — Commit N+1b. Audit query (grep -v AdminDbContext + grep -v
Migrations + grep -v comments) confirms zero call sites today match the
CER002 trigger surface. The UserJobRunner.cs IgnoreQueryFilters() call
uses AdminDbContext, which is the by-design pre-auth lookup path.

CER002 will ship at N+2 with no baseline-suppression entries.

Refs: docs/superpowers/specs/2026-05-26-stage-9-5c-roslyn-analyzers-design.md §6.2, §10"
```

If baseline > 0, commit the actual decorations with this message stem and list the decorated files.

---

## Task 4: Commit N+1c — RESERVED (empty)

**Goal:** Reserve the commit number in the chain for traceability. CER003 was withdrawn during planning (see spec §6.3).

### Steps

- [ ] **Step 1: Skip this commit**

No work to do. The spec's §10 table marks N+1c as `(intentionally empty — CER003 was withdrawn during planning; commit number reserved for traceability)`. The next commit is N+1d.

(Optionally, if subsequent tooling depends on a literal N+1c commit existing, create an empty commit:

```bash
git commit --allow-empty -m "chore(9.5c): N+1c reserved — CER003 withdrawn during planning

Stage 9.5c — Commit N+1c is empty. CER003 (class-level [Authorize]
enforcement) was withdrawn during planning 2026-05-26 after discovering
the existing architecture test 'Every_controller_action_declares_authorization_intent'
already enforces per-action authz intent at tighter granularity.

See: docs/superpowers/specs/2026-05-26-stage-9-5c-roslyn-analyzers-design.md §6.3, §12

No file changes."
```

Recommended: skip the empty commit unless a downstream consumer requires the sequence.)

---

## Task 5: Commit N+1d — TimeProvider DI + retro-decorate CER004 surface

**Goal:** Register `TimeProvider` in DI; replace `DateTime.UtcNow`/`DateTime.Now` reads with `_timeProvider.GetUtcNow().UtcDateTime` where injection is feasible; decorate the rest with `[AllowsWallClock("reason")]`.

**Files (estimated 25+ files modified):**
- Modify: `ProjectCeres/Program.cs` (add `builder.Services.AddSingleton(TimeProvider.System);`)
- Modify: `ProjectCeres/Common/Authentication/AuditLogWriter.cs` (constructor inject TimeProvider, replace UtcNow)
- Modify: `ProjectCeres/Common/Authentication/FailedLoginRecorder.cs`
- Modify: `ProjectCeres/Common/Authentication/LockoutUnlockService.cs` (5 hits)
- Modify: `ProjectCeres/Common/Authentication/EmailConfirmationService.cs` (9 hits)
- Modify: `ProjectCeres/Common/Authentication/EmailChangeService.cs` (14 hits)
- Modify: `ProjectCeres/Common/Authentication/PasswordResetService.cs` (13 hits)
- Modify: `ProjectCeres/Common/Authentication/PersistentCookieRotationMiddleware.cs` (3 hits)
- Modify: `ProjectCeres/Common/Authentication/UserBlockedIpMiddleware.cs` (1 hit)
- Modify: `ProjectCeres/Common/Authentication/SessionRevocationValidator.cs` (2 hits)
- Modify: `ProjectCeres/Common/Authentication/MfaBackupCodeService.cs` (2 hits)
- Modify: `ProjectCeres/Common/Authentication/TotpReplayGuard.cs` (2 hits)
- Modify: `ProjectCeres/Controllers/Api/AuthController.cs` (4 hits)
- Modify: `ProjectCeres/Services/CsvImportProfileService.cs` (5 hits)
- Modify: `ProjectCeres/Services/TransferReviewService.cs` (4 hits)
- Modify: `ProjectCeres/Services/ImportStagedTransactionService.cs` (3 hits)
- Modify: `ProjectCeres/Services/FileAttachmentService.cs` (2 hits)
- Modify: `ProjectCeres/Services/AccountService.cs` (2 hits)
- Modify: `ProjectCeres/Services/TransferService.cs` (1 hit)
- Modify: `ProjectCeres/Services/TransferDetectionService.cs` (1 hit)
- Modify: `ProjectCeres/Services/TransactionService.cs` (1 hit)
- Modify: `ProjectCeres/Services/RecurringTransactionService.cs` (1 hit)
- Modify: `ProjectCeres/Services/LiabilityPaymentService.cs` (1 hit)
- Modify: `ProjectCeres/Services/ImportService.cs` (1 hit)
- Modify: `ProjectCeres/Models/ApplicationUser.cs` (1 hit — property initialiser; OUT of CER004 scope per L3 but worth annotating with `[AllowsWallClock]` defensively — see Step 4)
- Modify: `ProjectCeres/ViewModels/CsvImportProfileViewModel.cs` (1 hit — view model computed property)

**Subagent-dispatchable?** Sequential. Although per-file work is independent, the TimeProvider DI registration in Program.cs must land FIRST, and many files share patterns that benefit from one consistent voice. If dispatched as multiple subagents, must wait for Program.cs change before per-file work begins.

### Steps

- [ ] **Step 1: Register `TimeProvider.System` in DI**

In `ProjectCeres/Program.cs`, find the existing `builder.Services.Add*` block (after the WebApplication.CreateBuilder line, before `var app = builder.Build();`). Add:

```csharp
builder.Services.AddSingleton(TimeProvider.System);
```

The exact line ordering: place it near other framework-foundational singletons (look for `AddSingleton<IClock>` or `AddHttpContextAccessor` and place near). If neither exists, place it right after the first `builder.Services.AddX` call.

- [ ] **Step 2: Build to verify DI registration compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Decide the per-file pattern**

For each file in the list, ONE of two patterns:

**Pattern A — Constructor inject `TimeProvider`** (preferred — passes CER004 by removing the violation entirely):

```csharp
public sealed class FooService(AppDbContext db, TimeProvider timeProvider) : IFooService
{
    public Task Bar() => SomeWorkAsync(timeProvider.GetUtcNow().UtcDateTime);
}
```

Replace every `DateTime.UtcNow` in the class body with `timeProvider.GetUtcNow().UtcDateTime` (or `.DateTime` if the local doesn't need UTC semantics — read each site).

**Pattern B — `[AllowsWallClock("reason")]` on the containing method** (when injection is infeasible — static methods, attribute initialisers, model defaults):

```csharp
using ProjectCeres.Analyzers.Annotations;

// ...

[AllowsWallClock("entity property initialiser — TimeProvider cannot be injected into POCO constructors")]
public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
```

For each file:
- Read the file.
- Identify if `TimeProvider` is injectable (it's a service class with a constructor) → Pattern A.
- If not (entity, view model with no constructor, static utility) → Pattern B.
- For services that already use primary constructors (`public sealed class Foo(...)`), Pattern A is one-line.
- For services with explicit constructors, add `TimeProvider timeProvider` as a parameter and store as `_timeProvider`.

- [ ] **Step 4: Special case — `ProjectCeres/Models/ApplicationUser.cs`**

Per spec L3, entity property initialisers in `ProjectCeres.Models` namespace are EXCLUDED from CER004. Strictly, no annotation is required. BUT: the spec's "excluded" comes via the analyzer's namespace check, not via the attribute. Some readers (including the future code-fix provider in 9.5j) may want to see the `[AllowsWallClock]` attribute as documentation of intent. Defensive recommendation: add the attribute even though it's excluded — it's a one-line doc of why the wall-clock is acceptable here.

```csharp
using ProjectCeres.Analyzers.Annotations;

namespace ProjectCeres.Models;

public class ApplicationUser : IdentityUser<Guid>
{
    [AllowsWallClock("entity property initialiser — EF Core materializer cannot inject TimeProvider")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 5: Apply Pattern A or B to each file in the list**

For each file in the "Files" list above, apply Pattern A or Pattern B per Step 3. Per file:
1. Read the file end-to-end (don't apply blindly).
2. Identify the DI shape (primary ctor, explicit ctor, static class, entity).
3. Apply the pattern.
4. Confirm no other `DateTime.UtcNow` / `DateTime.Now` is missed.

For files with many hits (EmailChangeService 14, PasswordResetService 13, EmailConfirmationService 9), Pattern A's single ctor change replaces all hits in one diff.

- [ ] **Step 6: Build after every 5–10 file modifications**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: green after each batch. If errors, investigate before continuing the next batch.

Common failure: forgetting to add `_timeProvider` field assignment in an explicit-ctor class. Symptom: `_timeProvider` cannot be resolved.

- [ ] **Step 7: Run full test suite**

Run: `dotnet test`
Expected: existing tests pass. If a test fails, the likely cause is a test that constructed the service directly without supplying `TimeProvider`. Fix the test by passing `TimeProvider.System` (production behaviour preserved) — NOT by mocking `TimeProvider` unless the test specifically wants deterministic time.

Hard rule per `feedback_never_skip_tests_to_make_them_pass`: if a test fails, fix the production code OR rewrite the assertion to assert what's now true. Do NOT add `[Skip="..."]`.

- [ ] **Step 8: Run residual CER004 audit**

After all modifications:

Run:
```bash
grep -rn 'DateTime\.\(UtcNow\|Now\)' ProjectCeres/ --include='*.cs' \
  | grep -v '/Migrations/' \
  | grep -v '/bin/\|/obj/' \
  | grep -v 'AllowsWallClock' \
  | wc -l
```

Count the result. This is the post-decoration CER004 baseline that will land in N+2's `.editorconfig`. Target: as low as possible. Realistically: ~10-30 sites remain (Models/ entity initialisers + ViewModels). Each of these MUST carry `[AllowsWallClock("reason")]` (per Step 4's defensive pattern) so the baseline-suppression file at N+2 is concretely scoped.

- [ ] **Step 9: Commit (one large commit, single PR scope)**

```bash
git add ProjectCeres/
git commit -m "feat(9.5c): TimeProvider DI + retro-decorate CER004 surface

Stage 9.5c — Commit N+1d. Largest retro-decoration commit. Registers
TimeProvider.System as a singleton in Program.cs; ~25 files migrate
from DateTime.UtcNow/.Now to TimeProvider.GetUtcNow().UtcDateTime
via constructor injection; remaining sites (entity initialisers,
view-model computed properties) carry [AllowsWallClock(reason)] with
the reason naming the injection infeasibility.

No behaviour change — TimeProvider.System reads the wall clock
identically to DateTime.UtcNow; the analyzer + future tests gain
the ability to swap a deterministic TimeProvider for test runs.

Residual CER004 violation count after this commit: <count from
step 8>. These are the baseline-suppression entries that will land
in N+2's .editorconfig.

Refs: docs/superpowers/specs/2026-05-26-stage-9-5c-roslyn-analyzers-design.md §6.4, §10"
```

---

## Task 6: Commit N+2 — Analyzers csproj + Tests csproj + ProjectReference wiring + .editorconfig baseline

**Goal:** Ship the analyzer DLL + the test project; wire the analyzer into `ProjectCeres` at `warning` severity; populate `.editorconfig` with the residual CER004 baseline.

**Files:**
- Create: `ProjectCeres.Analyzers/ProjectCeres.Analyzers.csproj`
- Create: `ProjectCeres.Analyzers/Diagnostics.cs`
- Create: `ProjectCeres.Analyzers/PreAuthScopeTransactionAnalyzer.cs` (CER001)
- Create: `ProjectCeres.Analyzers/IgnoreQueryFiltersOnUserOwnedAnalyzer.cs` (CER002)
- Create: `ProjectCeres.Analyzers/DateTimeWallClockAnalyzer.cs` (CER004)
- Create: `ProjectCeres.Analyzers/RlsBypassJustifiedTicketFormatAnalyzer.cs` (CER010)
- Create: `ProjectCeres.Analyzers/ResxParityGenerator.cs` (CER020)
- Create: `ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj`
- Create: `ProjectCeres.Analyzers.Tests/TestHelpers/CSharpAnalyzerVerifier.cs`
- Create: `ProjectCeres.Analyzers.Tests/CER001_PreAuthScopeTransactionAnalyzerTests.cs`
- Create: `ProjectCeres.Analyzers.Tests/CER002_IgnoreQueryFiltersOnUserOwnedAnalyzerTests.cs`
- Create: `ProjectCeres.Analyzers.Tests/CER004_DateTimeWallClockAnalyzerTests.cs`
- Create: `ProjectCeres.Analyzers.Tests/CER010_RlsBypassJustifiedTicketFormatAnalyzerTests.cs`
- Create: `ProjectCeres.Analyzers.Tests/CER020_ResxParityGeneratorTests.cs`
- Create: `.editorconfig` (root)
- Modify: `ProjectCeres.sln` (add 2 new projects)
- Modify: `ProjectCeres/ProjectCeres.csproj` (add analyzer ProjectReference)

**Subagent-dispatchable?** Sequential — analyzer scaffolding goes first, then tests, then `.editorconfig` baseline (which is computed from N+1d's residual count). Within this commit, the 5 analyzer files + their test files could be parallelised by 5 subagents if dispatched after the csproj + Diagnostics.cs land.

### Steps

- [ ] **Step 1: Create `ProjectCeres.Analyzers/ProjectCeres.Analyzers.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <IsPackable>false</IsPackable>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.11.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.11.0" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

Per spec L9 + research pitfall #1: `netstandard2.0` mandatory. `<EnforceExtendedAnalyzerRules>` is the in-build canary that catches forbidden API use (per research pitfall #2).

- [ ] **Step 2: Create `ProjectCeres.Analyzers/Diagnostics.cs` — the central descriptor registry**

```csharp
using Microsoft.CodeAnalysis;

namespace ProjectCeres.Analyzers;

internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor CER001_PreAuthScopeTransactionType = new(
        id: "CER001",
        title: "[PreAuthScope]-marked class must use BeginPreAuthUserScopeAsync, not BeginTransactionAsync",
        messageFormat: "Class '{0}' is marked [PreAuthScope] but calls BeginTransactionAsync directly. Use _db.BeginPreAuthUserScopeAsync(userId, ct) instead.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Pre-auth code paths must set the per-request user context before opening a transaction so RLS policies fire correctly. See ProjectCeres/Common/Authentication/PreAuthRlsScope.cs.");

    public static readonly DiagnosticDescriptor CER002_IgnoreQueryFiltersOnUserOwned = new(
        id: "CER002",
        title: "IgnoreQueryFilters() on IUserOwned via AppDbContext requires [RlsBypassJustified(ticket)]",
        messageFormat: "Call to IgnoreQueryFilters() on IUserOwned entity '{0}' via AppDbContext strips the per-user EF wall. Either route through AdminDbContext or add [RlsBypassJustified(\"CER-NNNN\")] to the containing method.",
        category: "Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "AppDbContext queries against IUserOwned entities must keep the EF query filter unless a documented bypass justification exists.");

    public static readonly DiagnosticDescriptor CER004_DateTimeWallClock = new(
        id: "CER004",
        title: "Use TimeProvider.GetUtcNow() instead of DateTime.UtcNow / DateTime.Now",
        messageFormat: "Direct read of '{0}' bypasses TimeProvider injection. Replace with _timeProvider.GetUtcNow().UtcDateTime or add [AllowsWallClock(\"reason\")] to the containing member.",
        category: "Reliability",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Production code should accept TimeProvider via DI so integration tests can pin time deterministically.");

    public static readonly DiagnosticDescriptor CER010_RlsBypassJustifiedTicketFormat = new(
        id: "CER010",
        title: "[RlsBypassJustified] ticket must match ^(CER|TICKET|ADR)-\\d+$",
        messageFormat: "[RlsBypassJustified(\"{0}\")] ticket does not match the required format. Use CER-NNNN (analyzer ID), TICKET-NNNN (issue tracker), or ADR-NNNN (architecture decision record).",
        category: "Style",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Lazy justifications (\"temp\", \"TODO\") undermine the bypass-justified audit trail.");

    public static readonly DiagnosticDescriptor CER020_ResxParityMissing = new(
        id: "CER020",
        title: "EN/ES resx parity violated — culture is missing a key its sibling has",
        messageFormat: "Resource file '{0}' is missing key '{1}' that exists in '{2}'.",
        category: "Localization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Translation keys must exist in every locale before merge. A missing translation surfaces as the literal key string at runtime.");
}
```

- [ ] **Step 3: Create `ProjectCeres.Analyzers/PreAuthScopeTransactionAnalyzer.cs` (CER001)**

```csharp
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ProjectCeres.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PreAuthScopeTransactionAnalyzer : DiagnosticAnalyzer
{
    private const string PreAuthScopeAttributeFullName = "ProjectCeres.Analyzers.Annotations.PreAuthScopeAttribute";
    private const string BeginTransactionMethodName = "BeginTransactionAsync";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.CER001_PreAuthScopeTransactionType);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
    {
        if (ctx.Node is not InvocationExpressionSyntax invocation) return;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) return;
        if (memberAccess.Name.Identifier.Text != BeginTransactionMethodName) return;

        // Walk up to the enclosing type declaration
        var enclosingType = invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (enclosingType is null) return;

        // Check if the enclosing type carries [PreAuthScope]
        var typeSymbol = ctx.SemanticModel.GetDeclaredSymbol(enclosingType);
        if (typeSymbol is null) return;

        var hasPreAuthScope = typeSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == PreAuthScopeAttributeFullName);

        if (!hasPreAuthScope) return;

        // Exclude PreAuthRlsScope.cs itself (the helper LEGITIMATELY calls BeginTransactionAsync)
        var filePath = invocation.SyntaxTree.FilePath ?? string.Empty;
        if (filePath.EndsWith("PreAuthRlsScope.cs", System.StringComparison.OrdinalIgnoreCase)) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.CER001_PreAuthScopeTransactionType,
            invocation.GetLocation(),
            typeSymbol.Name));
    }
}
```

- [ ] **Step 4: Create `ProjectCeres.Analyzers/IgnoreQueryFiltersOnUserOwnedAnalyzer.cs` (CER002)**

```csharp
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ProjectCeres.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IgnoreQueryFiltersOnUserOwnedAnalyzer : DiagnosticAnalyzer
{
    private const string IgnoreQueryFiltersMethodName = "IgnoreQueryFilters";
    private const string IUserOwnedFullName = "ProjectCeres.Common.IUserOwned";
    private const string AppDbContextFullName = "ProjectCeres.Data.AppDbContext";
    private const string RlsBypassJustifiedAttributeFullName = "ProjectCeres.Analyzers.Annotations.RlsBypassJustifiedAttribute";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.CER002_IgnoreQueryFiltersOnUserOwned);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext ctx)
    {
        if (ctx.Operation is not IInvocationOperation invocation) return;
        if (invocation.TargetMethod.Name != IgnoreQueryFiltersMethodName) return;

        // Receiver must be a Queryable<T> where T implements IUserOwned
        var receiverType = invocation.Instance?.Type
            ?? (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value.Type : null);
        if (receiverType is not INamedTypeSymbol named) return;

        var elementType = named.TypeArguments.FirstOrDefault();
        if (elementType is null) return;

        var implementsUserOwned = elementType.AllInterfaces
            .Any(i => i.ToDisplayString() == IUserOwnedFullName);
        if (!implementsUserOwned) return;

        // Check the containing DbContext type — must be AppDbContext (not AdminDbContext)
        // Find the enclosing field/property receiver and check its type
        var containingType = invocation.SemanticModel?.GetEnclosingSymbol(invocation.Syntax.SpanStart) as IMethodSymbol;
        if (containingType is null) return;

        // Walk the containing class's fields/properties to find which DbContext is in use
        var containingClass = containingType.ContainingType;
        var usesAppDbContext = containingClass?.GetMembers()
            .OfType<IFieldSymbol>()
            .Any(f => f.Type.ToDisplayString() == AppDbContextFullName) == true
            || containingClass?.GetMembers()
                .OfType<IPropertySymbol>()
                .Any(p => p.Type.ToDisplayString() == AppDbContextFullName) == true;

        // Also check primary-constructor parameters (modern C# pattern)
        if (!usesAppDbContext && containingClass?.InstanceConstructors.Any() == true)
        {
            usesAppDbContext = containingClass.InstanceConstructors
                .SelectMany(c => c.Parameters)
                .Any(p => p.Type.ToDisplayString() == AppDbContextFullName);
        }

        if (!usesAppDbContext) return;

        // Check if the enclosing method has [RlsBypassJustified]
        var hasJustification = containingType.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == RlsBypassJustifiedAttributeFullName);
        if (hasJustification) return;

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.CER002_IgnoreQueryFiltersOnUserOwned,
            invocation.Syntax.GetLocation(),
            elementType.Name));
    }
}
```

- [ ] **Step 5: Create `ProjectCeres.Analyzers/DateTimeWallClockAnalyzer.cs` (CER004)**

```csharp
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ProjectCeres.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DateTimeWallClockAnalyzer : DiagnosticAnalyzer
{
    private const string DateTimeFullName = "System.DateTime";
    private const string AllowsWallClockAttributeFullName = "ProjectCeres.Analyzers.Annotations.AllowsWallClockAttribute";
    private const string ModelsNamespacePrefix = "ProjectCeres.Models";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.CER004_DateTimeWallClock);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext ctx)
    {
        if (ctx.Node is not MemberAccessExpressionSyntax memberAccess) return;
        var name = memberAccess.Name.Identifier.Text;
        if (name != "UtcNow" && name != "Now") return;

        var typeInfo = ctx.SemanticModel.GetTypeInfo(memberAccess.Expression);
        if (typeInfo.Type?.ToDisplayString() != DateTimeFullName) return;

        // Exclude /Migrations/ files
        var filePath = memberAccess.SyntaxTree.FilePath ?? string.Empty;
        if (filePath.Contains("/Migrations/") || filePath.Contains("\\Migrations\\")) return;

        // Exclude property initialisers inside ProjectCeres.Models namespace
        var enclosingType = memberAccess.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (enclosingType is not null)
        {
            var typeSymbol = ctx.SemanticModel.GetDeclaredSymbol(enclosingType);
            var ns = typeSymbol?.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (ns.StartsWith(ModelsNamespacePrefix, System.StringComparison.Ordinal))
            {
                // Check if we're inside a property initialiser
                var enclosingProperty = memberAccess.FirstAncestorOrSelf<PropertyDeclarationSyntax>();
                if (enclosingProperty?.Initializer is not null) return;
            }
        }

        // Check enclosing member for [AllowsWallClock]
        var enclosingMember = memberAccess.FirstAncestorOrSelf<MemberDeclarationSyntax>();
        if (enclosingMember is not null)
        {
            var memberSymbol = ctx.SemanticModel.GetDeclaredSymbol(enclosingMember);
            if (memberSymbol?.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == AllowsWallClockAttributeFullName) == true)
            {
                return;
            }
        }

        ctx.ReportDiagnostic(Diagnostic.Create(
            Diagnostics.CER004_DateTimeWallClock,
            memberAccess.GetLocation(),
            $"DateTime.{name}"));
    }
}
```

- [ ] **Step 6: Create `ProjectCeres.Analyzers/RlsBypassJustifiedTicketFormatAnalyzer.cs` (CER010)**

```csharp
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ProjectCeres.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RlsBypassJustifiedTicketFormatAnalyzer : DiagnosticAnalyzer
{
    private const string RlsBypassJustifiedAttributeFullName = "ProjectCeres.Analyzers.Annotations.RlsBypassJustifiedAttribute";
    private static readonly Regex TicketFormat = new(@"^(CER|TICKET|ADR)-\d+$", RegexOptions.Compiled);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Diagnostics.CER010_RlsBypassJustifiedTicketFormat);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethodAttributes, SymbolKind.Method);
    }

    private static void AnalyzeMethodAttributes(SymbolAnalysisContext ctx)
    {
        if (ctx.Symbol is not IMethodSymbol method) return;

        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != RlsBypassJustifiedAttributeFullName) continue;
            if (attr.ConstructorArguments.Length == 0) continue;
            if (attr.ConstructorArguments[0].Value is not string ticket) continue;
            if (TicketFormat.IsMatch(ticket)) continue;

            var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                ?? method.Locations.FirstOrDefault()
                ?? Location.None;

            ctx.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.CER010_RlsBypassJustifiedTicketFormat,
                location,
                ticket));
        }
    }
}
```

(Note: `using System.Linq;` is needed for `FirstOrDefault()`. Add it.)

- [ ] **Step 7: Create `ProjectCeres.Analyzers/ResxParityGenerator.cs` (CER020)**

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace ProjectCeres.Analyzers;

[Generator(LanguageNames.CSharp)]
public sealed class ResxParityGenerator : IIncrementalGenerator
{
    private static readonly Regex ResxFileName = new(@"^(.+)\.(en|es)\.resx$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var resxFiles = context.AdditionalTextsProvider
            .Where(f => f.Path.EndsWith(".resx", System.StringComparison.OrdinalIgnoreCase));

        var grouped = resxFiles.Collect();

        context.RegisterSourceOutput(grouped, (spc, files) =>
        {
            var bases = new Dictionary<string, Dictionary<string, AdditionalText>>();

            foreach (var file in files)
            {
                var name = System.IO.Path.GetFileName(file.Path);
                var match = ResxFileName.Match(name);
                if (!match.Success) continue;

                var baseName = match.Groups[1].Value;
                var culture = match.Groups[2].Value.ToLowerInvariant();

                if (!bases.TryGetValue(baseName, out var cultureMap))
                {
                    cultureMap = new Dictionary<string, AdditionalText>();
                    bases[baseName] = cultureMap;
                }
                cultureMap[culture] = file;
            }

            foreach (var kvp in bases)
            {
                var cultureMap = kvp.Value;
                if (cultureMap.Count < 2) continue;

                var keysByCulture = cultureMap.ToDictionary(
                    c => c.Key,
                    c => ParseKeys(c.Value));

                var allKeys = keysByCulture.SelectMany(k => k.Value).Distinct().ToList();

                foreach (var (culture, file) in cultureMap)
                {
                    var missing = allKeys.Where(k => !keysByCulture[culture].Contains(k)).ToList();
                    foreach (var missingKey in missing)
                    {
                        var present = cultureMap.First(c => keysByCulture[c.Key].Contains(missingKey));
                        var diagnostic = Diagnostic.Create(
                            Diagnostics.CER020_ResxParityMissing,
                            Location.None,
                            System.IO.Path.GetFileName(file.Path),
                            missingKey,
                            System.IO.Path.GetFileName(present.Value.Path));
                        spc.ReportDiagnostic(diagnostic);
                    }
                }
            }

            spc.AddSource("ResxParityCheck.g.cs",
                "// <auto-generated>resx-parity-check executed by ProjectCeres.Analyzers.ResxParityGenerator</auto-generated>\n");
        });
    }

    private static HashSet<string> ParseKeys(AdditionalText file)
    {
        var keys = new HashSet<string>();
        try
        {
            var content = file.GetText()?.ToString();
            if (content is null) return keys;
            var doc = XDocument.Parse(content);
            foreach (var data in doc.Descendants("data"))
            {
                var name = data.Attribute("name")?.Value;
                if (name is not null) keys.Add(name);
            }
        }
        catch
        {
            // Malformed XML — silent for now; future CER021 can report this
        }
        return keys;
    }
}
```

- [ ] **Step 8: Add Annotations as ProjectReference from Analyzers**

The analyzers reference attribute symbols by their full name string, so a project-reference isn't strictly needed for the symbol comparison. But the test project will need to compile sample source code that USES the attributes, so the Annotations reference shipped via the consumer's transitive graph is enough. Skip this step UNLESS Step 12's tests show otherwise.

- [ ] **Step 9: Build the Analyzers project**

Run: `dotnet build ProjectCeres.Analyzers/ProjectCeres.Analyzers.csproj`
Expected: `Build succeeded. 0 Error(s)`. Warnings about RS-prefixed analyzer rules are EXPECTED and acceptable (Roslyn's own analyzer-analyzer fires defensively); only fail if a CSnnnn error appears.

- [ ] **Step 10: Create `ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.7.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.7" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Analyzer.Testing" Version="1.1.2" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.11.0" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.11.0" />
    <PackageReference Include="FluentAssertions" Version="8.9.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ProjectCeres.Analyzers\ProjectCeres.Analyzers.csproj" />
    <ProjectReference Include="..\ProjectCeres.Analyzers.Annotations\ProjectCeres.Analyzers.Annotations.csproj" />
  </ItemGroup>

</Project>
```

The `Microsoft.CodeAnalysis.CSharp.Workspaces` is pinned per research pitfall on .NET 10 — explicit version >= analyzer's.

- [ ] **Step 11: Create `ProjectCeres.Analyzers.Tests/TestHelpers/CSharpAnalyzerVerifier.cs`**

```csharp
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace ProjectCeres.Analyzers.Tests.TestHelpers;

public static class CSharpAnalyzerVerifier<TAnalyzer> where TAnalyzer : DiagnosticAnalyzer, new()
{
    public class Test : CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
    {
        public Test()
        {
            // Reference the Annotations DLL so test sources can [PreAuthScope] / [AllowsWallClock] / [RlsBypassJustified]
            ReferenceAssemblies = ReferenceAssemblies.Net.Net100;
        }
    }

    public static System.Threading.Tasks.Task VerifyAnalyzerAsync(string source, params DiagnosticResult[] expected)
    {
        var test = new Test { TestCode = source };
        test.ExpectedDiagnostics.AddRange(expected);
        return test.RunAsync();
    }
}
```

- [ ] **Step 12: Write `ProjectCeres.Analyzers.Tests/CER001_PreAuthScopeTransactionAnalyzerTests.cs`**

```csharp
using Microsoft.CodeAnalysis.Testing;
using ProjectCeres.Analyzers.Tests.TestHelpers;
using Xunit;
using Verifier = ProjectCeres.Analyzers.Tests.TestHelpers.CSharpAnalyzerVerifier<ProjectCeres.Analyzers.PreAuthScopeTransactionAnalyzer>;

namespace ProjectCeres.Analyzers.Tests;

public class CER001_PreAuthScopeTransactionAnalyzerTests
{
    [Fact]
    public async Task Fires_When_PreAuthScope_Class_Uses_BeginTransactionAsync()
    {
        var source = @"
using System.Threading.Tasks;
using ProjectCeres.Analyzers.Annotations;

namespace ProjectCeres.Analyzers.Annotations
{
    public class PreAuthScopeAttribute : System.Attribute { }
}

namespace Test
{
    public class FakeDb
    {
        public FakeDatabase Database => new();
    }

    public class FakeDatabase
    {
        public Task BeginTransactionAsync() => Task.CompletedTask;
    }

    [ProjectCeres.Analyzers.Annotations.PreAuthScope]
    public class MarkedClass
    {
        private readonly FakeDb _db = new();
        public async Task Bar() => await _db.Database.{|#0:BeginTransactionAsync|}();
    }
}
";
        var expected = new DiagnosticResult(Diagnostics.CER001_PreAuthScopeTransactionType)
            .WithLocation(0)
            .WithArguments("MarkedClass");

        await Verifier.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task NoFire_When_Class_Without_PreAuthScope_Uses_BeginTransactionAsync()
    {
        var source = @"
using System.Threading.Tasks;

namespace Test
{
    public class FakeDb { public FakeDatabase Database => new(); }
    public class FakeDatabase { public Task BeginTransactionAsync() => Task.CompletedTask; }

    public class PlainClass
    {
        private readonly FakeDb _db = new();
        public async Task Bar() => await _db.Database.BeginTransactionAsync();
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NoFire_When_PreAuthScope_Class_Uses_BeginPreAuthUserScopeAsync()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using ProjectCeres.Analyzers.Annotations;

namespace ProjectCeres.Analyzers.Annotations
{
    public class PreAuthScopeAttribute : System.Attribute { }
}

namespace Test
{
    public static class ScopeExtensions
    {
        public static Task BeginPreAuthUserScopeAsync(this FakeDb db, Guid userId) => Task.CompletedTask;
    }
    public class FakeDb { }

    [ProjectCeres.Analyzers.Annotations.PreAuthScope]
    public class MarkedClass
    {
        private readonly FakeDb _db = new();
        public async Task Bar() => await _db.BeginPreAuthUserScopeAsync(Guid.NewGuid());
    }
}
";
        await Verifier.VerifyAnalyzerAsync(source);
    }
}
```

- [ ] **Step 13: Write `ProjectCeres.Analyzers.Tests/CER002_IgnoreQueryFiltersOnUserOwnedAnalyzerTests.cs`**

Same shape as Task 12 — write positive case (AppDbContext + IUserOwned + IgnoreQueryFilters fires), negative case (AdminDbContext + IUserOwned + IgnoreQueryFilters doesn't fire), excluded-path case (AppDbContext + non-IUserOwned doesn't fire), and the justified case (method with valid `[RlsBypassJustified("CER-1234")]` doesn't fire).

Pattern matches Task 12; the source fixtures must declare fake `AppDbContext`, `AdminDbContext`, `IUserOwned`, and the attribute types inline because the analyzer compares by full name string.

- [ ] **Step 14: Write `ProjectCeres.Analyzers.Tests/CER004_DateTimeWallClockAnalyzerTests.cs`**

Tests per spec §6.4:
- (a) Service method body with `DateTime.UtcNow` → fires
- (b) Property initialiser in `ProjectCeres.Models` namespace → no fire
- (c) `/Migrations/` file path → no fire
- (d) Method carrying `[AllowsWallClock("reason")]` → no fire
- (e) Constructor in non-Models namespace → fires unless attributed

- [ ] **Step 15: Write `ProjectCeres.Analyzers.Tests/CER010_RlsBypassJustifiedTicketFormatAnalyzerTests.cs`**

Tests per spec §6.5:
- (a) `[RlsBypassJustified("CER-1234")]` → no fire
- (b) `[RlsBypassJustified("TICKET-99")]` → no fire
- (c) `[RlsBypassJustified("ADR-0042")]` → no fire
- (d) `[RlsBypassJustified("temp")]` → fires
- (e) `[RlsBypassJustified("")]` → fires
- (f) `[RlsBypassJustified("CER-NNNN")]` (no digits) → fires

- [ ] **Step 16: Write `ProjectCeres.Analyzers.Tests/CER020_ResxParityGeneratorTests.cs`**

Generator tests need a slightly different harness — `CSharpSourceGeneratorTest<TGenerator, TVerifier>` from `Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing`. Cover:
- (a) EN + ES with identical keys → no diagnostic
- (b) EN missing a key ES has → diagnostic
- (c) ES missing a key EN has → diagnostic
- (d) Only EN file present → no diagnostic (parity only fires when ≥2 cultures present)

- [ ] **Step 17: Build + run all analyzer tests**

Run: `dotnet test ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj`
Expected: all tests pass. If a test fails, the analyzer or test source has a bug — fix the analyzer first (production code wins over test); rewrite the assertion only if the contract intentionally differs.

- [ ] **Step 18: Add both new projects to ProjectCeres.sln**

Run:
```bash
dotnet sln ProjectCeres.sln add ProjectCeres.Analyzers/ProjectCeres.Analyzers.csproj
dotnet sln ProjectCeres.sln add ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj
```

Expected: both projects added.

- [ ] **Step 19: Wire the analyzer into ProjectCeres.csproj**

In `ProjectCeres/ProjectCeres.csproj`, add a new `<ItemGroup>` adjacent to the existing Annotations reference:

```xml
  <ItemGroup>
    <ProjectReference Include="..\ProjectCeres.Analyzers\ProjectCeres.Analyzers.csproj">
      <OutputItemType>Analyzer</OutputItemType>
      <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
    </ProjectReference>
  </ItemGroup>
```

Per F2 + research pitfall #3: `<ReferenceOutputAssembly>false</ReferenceOutputAssembly>` is required so the analyzer DLL doesn't get copied as a regular reference.

- [ ] **Step 20: Run CER004 baseline audit + compute residual count**

Run:
```bash
dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep 'warning CER004' | wc -l
```

Save the count as `<RESIDUAL_CER004_COUNT>`.

Also list the file paths:

```bash
dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep 'warning CER004' | awk -F'(' '{print $1}' | sort -u
```

- [ ] **Step 21: Create root `.editorconfig` with baseline-suppression sections**

```
root = true

[*.cs]
# C# minimal defaults — matches existing implicit conventions
indent_style = space
indent_size = 4
end_of_line = lf
insert_final_newline = true
charset = utf-8

# ─────────────────────────────────────────────────────────────────────────
# Per-analyzer baseline-suppression sections (Stage 9.5c, per C-3)
# Each entry has a one-line reason. Suppressed lines drain to zero before
# the warning→error flip at N+M+1 per C-2.
# ─────────────────────────────────────────────────────────────────────────

# CER004 — Stage 9.5c baseline. Sites pending TimeProvider migration.
# Drain progress: grep -c 'dotnet_diagnostic.CER004' .editorconfig
```

Then, for each file in the Step 20 list, add a section:

```
[ProjectCeres/Common/Authentication/<file>.cs]
dotnet_diagnostic.CER004.severity = none
```

Repeat for all residual files. The exact list comes from Step 20's output.

- [ ] **Step 22: Build to verify the baseline-suppression works**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep 'CER'`

Expected: ZERO CER warnings/errors. The suppressed files are silent; all other files have been retro-decorated in N+1a / N+1d.

If any `CER` warning leaks, that file was missed in the retro-decoration sweep. Add it to `.editorconfig` (defensive) OR go back to N+1d and add the missing decoration (correct).

- [ ] **Step 23: Run full test suite**

Run: `dotnet test`
Expected: green. All 1098+ existing tests pass PLUS the new analyzer test count (~20-30 new tests).

- [ ] **Step 24: Verify BuildTailwind still fires (per F3)**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep -i tailwind`
Expected: see `pnpm run build:css` in output. If absent, the BuildTailwind target broke — investigate before committing.

- [ ] **Step 25: Commit**

```bash
git add ProjectCeres.Analyzers/ \
        ProjectCeres.Analyzers.Tests/ \
        ProjectCeres.sln \
        ProjectCeres/ProjectCeres.csproj \
        .editorconfig
git commit -m "feat(9.5c): ship 4 analyzers + resx parity generator + baseline suppressions

Stage 9.5c — Commit N+2. Analyzers ship at warning severity per C-2.

Files:
- ProjectCeres.Analyzers/ — 4 DiagnosticAnalyzer classes + 1 IIncrementalGenerator + Diagnostics descriptor registry
- ProjectCeres.Analyzers.Tests/ — Verifier<T> harness + per-analyzer test fixtures
- ProjectCeres/ProjectCeres.csproj — ProjectReference with OutputItemType=Analyzer + ReferenceOutputAssembly=false (per F2 / research pitfall #3)
- .editorconfig — per-file baseline-suppression sections for residual CER004 sites pending TimeProvider migration

CER004 baseline at this commit: <N> suppressed sites. Drain via sibling
commits (N+3..N+M); warning→error flip lands at N+M+1 once baseline = 0
AND 48h have elapsed.

CER020 (resx parity) ships at error severity from day 1 — current parity is
30/30 keys, no diagnostics fire.

Refs: docs/superpowers/specs/2026-05-26-stage-9-5c-roslyn-analyzers-design.md §3 C-2 C-3, §4 F2 F3, §5, §6, §9, §10"
```

---

## Task 7: Commits N+3 … N+M — Baseline drain (CER004 sites → TimeProvider)

**Goal:** Migrate each suppressed CER004 site to `TimeProvider` injection (or to `[AllowsWallClock]` if injection is infeasible). Each commit removes the corresponding `.editorconfig` suppression line.

**Files (one or more per commit):** the residual CER004 site files from Task 6 Step 20.

**Subagent-dispatchable?** Parallel batches. Each suppressed file is independent. Dispatch 5-10 subagents at a time, each handling one file. Reconcile `.editorconfig` deletions sequentially (avoid merge conflicts).

### Steps (repeat per file or per batch)

- [ ] **Step 1: Pick a file from `.editorconfig`'s suppression list**

```bash
grep -A 1 'dotnet_diagnostic.CER004' .editorconfig | grep '^\[' | head -1
```

This returns the next suppressed file path.

- [ ] **Step 2: Migrate the file to TimeProvider**

Per Task 5 Pattern A (constructor inject TimeProvider, replace UtcNow) or Pattern B (annotate with `[AllowsWallClock("reason")]`).

- [ ] **Step 3: Build + verify the CER004 warning is gone**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep "<file-path>" | grep CER004`
Expected: no output (warning gone).

- [ ] **Step 4: Delete the corresponding `.editorconfig` section**

Remove the `[<file-path>]` + `dotnet_diagnostic.CER004.severity = none` lines from `.editorconfig`.

- [ ] **Step 5: Build to confirm no CER004 leaks**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj 2>&1 | grep CER004 | wc -l`
Expected: same count as before the drain step (the just-migrated file's warning was suppressed; deleting the suppression now leaves zero ALSO because the file was fixed).

- [ ] **Step 6: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~ProjectCeres.Tests.Unit"`
Expected: green.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/<modified-file> .editorconfig
git commit -m "chore(9.5c): drain CER004 baseline — <file-name>

Stage 9.5c — Baseline drain commit (one of N). Migrates <file> from
DateTime.UtcNow to TimeProvider injection (or annotates with
[AllowsWallClock] where injection is infeasible). Deletes the
corresponding suppression from .editorconfig.

Remaining suppressed sites: <grep -c 'dotnet_diagnostic.CER004' .editorconfig>"
```

- [ ] **Step 8: Repeat until baseline = 0**

Run: `grep -c 'dotnet_diagnostic.CER004' .editorconfig`
When the count reaches 0, the baseline is drained. Proceed to Task 8.

---

## Task 8: Commit N+M+1 — Flip warning→error + tick roadmap + cross-ref 9.1.6 tripwire

**Goal:** Once 48h have elapsed since N+2 AND baseline = 0 (both C-2 conditions met), flip the analyzer severity to `error`. Update the roadmap to close 9.5c. Cross-reference 9.1.6's tripwire bullet.

**Files:**
- Modify: `.editorconfig` (per-analyzer severity = error for CER001/CER002/CER004/CER010)
- Modify: `docs/roadmap-phase-three.md` (tick Stage 9.5h's 9.5c row + update 9.1.6 line 1179)

**Subagent-dispatchable?** Sequential — final close-out commit.

### Steps

- [ ] **Step 1: Verify both C-2 conditions are met**

(a) 48h since N+2:
```bash
git log -1 --format=%ai <commit-N+2-sha>
```
Confirm at least 48 hours have passed since that timestamp.

(b) Baseline drained:
```bash
grep -c 'dotnet_diagnostic.CER' .editorconfig
```
Expected: `0`.

If either condition is not met, DO NOT flip. Continue draining (Task 7) or wait.

- [ ] **Step 2: Edit `.editorconfig` to flip severity to error**

Add to `.editorconfig` (after the C# minimal defaults section):

```
# Stage 9.5c — Per-analyzer severity flip from warning to error
# (N+M+1, after 48h soak AND baseline drained per C-2)
dotnet_diagnostic.CER001.severity = error
dotnet_diagnostic.CER002.severity = error
dotnet_diagnostic.CER004.severity = error
dotnet_diagnostic.CER010.severity = error
# CER020 (resx parity) was already at error from day 1
```

- [ ] **Step 3: Build to verify everything is still green at error severity**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: `Build succeeded. 0 Error(s)`. (If any CER error fires, baseline was not actually drained — go back to Task 7.)

- [ ] **Step 4: Update `docs/roadmap-phase-three.md` § Stage 9.5h — tick the 9.5c verification line**

Find the `- [ ] 9.5c —` line (around line 1235-1240) and change `[ ]` to `[x]`. Add a one-line note at the end of the entry: `Closed YYYY-MM-DD; commit chain: <N> → <N+M+1>.`

- [ ] **Step 5: Update Stage 9.1.6's tripwire bullet (line 1179) — cross-reference 9.5c**

Find the `- [ ] **Tripwire (deferred to its own stage):** open a sibling roadmap entry under Batch 5...` line at line ~1179. Add a one-line cross-ref:

```
> **Partial realisation (2026-05-26 via Stage 9.5c):** `Directory.Build.props`
> + root `.editorconfig` shipped with CER001/CER002/CER004/CER010 at error
> severity. Stage 9.5c does NOT promote IDE0001 to a warning — that remains
> the unique tripwire for this stage to discharge in Batch 5.
```

- [ ] **Step 6: Build + test final time**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj && dotnet test`
Expected: green.

- [ ] **Step 7: Commit (close-out)**

```bash
git add .editorconfig docs/roadmap-phase-three.md
git commit -m "feat(9.5c): flip CER001/CER002/CER004/CER010 to error severity — Stage 9.5c closed

Stage 9.5c — Commit N+M+1. Final close-out.

C-2 conditions met:
- 48h soak elapsed since N+2 (commit <sha>)
- Baseline drained to zero (grep -c 'dotnet_diagnostic.CER' .editorconfig = 0)

Per-analyzer severity flipped to error. Future violations of CER001 /
CER002 / CER004 / CER010 will fail dotnet build. CER020 (resx parity)
was at error from day 1.

Roadmap: Stage 9.5h sub-stage 9.5c flipped to [x]. Stage 9.1.6 tripwire
bullet cross-references 9.5c as the partial realisation per pre-flight
finding F1.

Next in Stage 9.5h: 9.5k (codified subagent definitions) per the L1
ordering revised 2026-05-26.

Refs: docs/superpowers/specs/2026-05-26-stage-9-5c-roslyn-analyzers-design.md §3 C-2, §4 F1, §10, §11"
```

- [ ] **Step 8: Verify Stage 9.5h close-out gate isn't tripped**

Per Phase E (HARD), the close-out gate fires when 9.5h's `## Stage 9.5h` heading sees its sub-stages all ticked. After this commit, 9.5c is `[x]` but 9.5b, 9.5d, 9.5e, 9.5f, 9.5g, 9.5i, 9.5j, 9.5k are still `[ ]` — Stage 9.5h itself is NOT closed; only 9.5c is. Verify the roadmap reflects this (Stage 9.5h header still reads `⚠️ In progress`, not `✅ Done`).

---

## Self-Review (post-write checklist)

**1. Spec coverage check:**
- ✅ §1 Problem — addressed by every commit, named in commit messages
- ✅ §2 Locked decisions L1-L10 — every binding decision is enforced by a specific task step
- ✅ §3 Conditions C-1/C-2/C-3 — C-1 in Task 1 (annotations before analyzers), C-2 in Task 8 (48h + drain), C-3 in Task 6 Step 21 (baseline file)
- ✅ §4 Findings F1/F2/F3 — F1 in Task 1 (Directory.Build.props) + Task 8 Step 5 (cross-ref), F2 in Task 6 Step 19 (ProjectReference shape), F3 in Task 6 Step 24 (BuildTailwind check)
- ✅ §5 Architecture — 3 new csprojs all created in Task 1 + Task 6
- ✅ §6.1 CER001 — analyzer in Task 6 Step 3, tests in Task 6 Step 12, retro-decoration in Task 2
- ✅ §6.2 CER002 — analyzer in Task 6 Step 4, tests in Task 6 Step 13, baseline-zero verification in Task 3
- ✅ §6.3 CER003 — WITHDRAWN; tracked in Task 4
- ✅ §6.4 CER004 — analyzer in Task 6 Step 5, tests in Task 6 Step 14, retro-decoration in Task 5
- ✅ §6.5 CER010 — analyzer in Task 6 Step 6, tests in Task 6 Step 15
- ✅ §6.6 CER020 — generator in Task 6 Step 7, tests in Task 6 Step 16
- ✅ §10 Rollout (commit chain) — every commit row has a Task
- ✅ §11 Verification checklist — entries map to specific build/test commands in tasks
- ✅ §12 Out-of-scope routing — sub-stages 9.5f/g/i/j routing remains untouched; this plan ships 9.5c only

**2. Placeholder scan:**
- No "TBD" / "TODO" / "implement later" / "fill in details" in any step
- Step 13, 14, 15, 16 reference "Pattern matches Task 12" — acceptable because those tasks provide concrete test shapes; the body work is to instantiate the same pattern with different per-analyzer cases enumerated in spec §6
- All code blocks are complete and ready to compile

**3. Type consistency check:**
- `PreAuthScopeAttribute` / `RlsBypassJustifiedAttribute` / `AllowsWallClockAttribute` / `RequiresAdminContextAttribute` — names used consistently across Tasks 1, 2, 3, 5, 6
- `Diagnostics.CER001_PreAuthScopeTransactionType` / `CER002_IgnoreQueryFiltersOnUserOwned` / etc. — names used consistently across Tasks 6 Steps 2, 3, 4, 5, 6, 7, 12-16
- `BeginPreAuthUserScopeAsync` / `BeginTransactionAsync` — names used consistently
- `_timeProvider` / `TimeProvider.GetUtcNow().UtcDateTime` — pattern used consistently in Task 5
- `.editorconfig` baseline section format — consistent between Task 6 Step 21 (create) and Task 7 Step 4 (drain)

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-26-stage-9-5c-roslyn-analyzers-impl.md`. Two execution options:

**1. Subagent-Driven (recommended)** — Dispatch a fresh subagent per task, review between tasks, fast iteration. Particularly suited for this plan because:
- Tasks 2 + 3 are parallel (N+1a and N+1b).
- Task 6 sub-steps 12-16 (per-analyzer tests) are parallel after the analyzer DLLs land.
- Task 7 baseline-drain steps are embarrassingly parallel (one subagent per file).

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints. Slower but lower coordination cost.

**Which approach?**
