# Stage 9.5j — Code-Fix Providers for CER001 & CER004 — Design

**Date:** 2026-06-10
**Stage:** 9.5j (last analyzer-family sub-stage of the 9.5h hardening container)
**Status:** Design — approved section-by-section 2026-06-10

---

## 1. Scope (and the CER010 re-scope)

This stage ships the IDE lightbulb quick-fix actions that appear next to two of the
existing analyzer warnings, so a developer resolves the warning with one keystroke
instead of typing the replacement by hand.

**The deliverable is two code-fix providers, not three** — a change from the roadmap's
"CER001 / CER004 / CER010" listing:

| Diagnostic | Ships a fix? | Behavior |
|---|---|---|
| **CER004** (`DateTime.UtcNow` / `.Now`) | **Yes — safe** | Rewrites to `_timeProvider.GetUtcNow().UtcDateTime`, offered **only** when an instance field named exactly `_timeProvider` of type `System.TimeProvider` (or a subtype) exists in the enclosing class. No such field → no fix is registered (the warning stays). |
| **CER001** (wrong transaction call) | **Yes — placeholder** | Rewrites `….BeginTransactionAsync()` to `….BeginPreAuthUserScopeAsync(userId, ct)` with both identifiers deliberately undeclared, producing `CS0103` "name does not exist" errors as the developer's to-do list. Build stays red on purpose. |
| **CER010** (bad audit-trail ticket) | **No fix** | A format-valid fake ticket (e.g. `CER-9999`) would pass the analyzer **and** silently corrupt the bypass-justified audit trail — strictly worse than the warning it removes. No mechanically-correct value exists, so no fix is offered. |

### 1.1 Why CER010 ships no fix (re-scope from the roadmap premise)

The roadmap (`docs/roadmap-phase-three.md`, the 9.5j rationale row + checklist line)
listed CER010 as "ticket-format completion." That premise does not survive contact with
the rule's purpose. CER010 exists so that every `[RlsBypassJustified]` carries a *real*
audit reference (`CER-NNNN` / `TICKET-NNNN` / `ADR-NNNN`). A code-fix can only invent a
*format-valid* string — and a format-valid invented ticket (`CER-9999`) would:

1. Pass the CER010 format check, removing the warning.
2. Point the audit trail at a ticket that does not exist.

That is a worse state than the lazy `"temp"` the rule was built to catch, because the
warning is now gone. The only correct ticket is one the human knows; there is nothing
mechanical to substitute. So CER010 is explicitly out of scope, recorded here, in
ADR-0077, the roadmap line, and the changelog (the same four-place re-scope sync used
for 9.5i).

This mirrors the 9.5i precedent: when the roadmap's mechanical premise is falsified by the
rule's actual semantics, the stage re-scopes and records the re-scope rather than shipping
a fix that defeats the rule.

### 1.2 Out of scope (no mechanical fix)

CER002 (IgnoreQueryFilters bypass), CER003 (authorization-intent), CER005 (TokenLookup
discipline), CER006 (PreAuthScope marker), CER020 (resx parity) — none has a single
mechanically-correct replacement. They stay analyzer-only. This matches the 9.5c brainstorm
Section 4 boundary that excluded code-fix providers from the analyzer-shipping stage.

---

## 2. Placement, dependency, and what does NOT change

### 2.1 No new project, no new wiring in the main app

`ProjectCeres/ProjectCeres.csproj:30-34` already references the analyzer assembly as
`OutputItemType=Analyzer` with `ReferenceOutputAssembly=false` and `PrivateAssets=all`.
Roslyn loads `CodeFixProvider` implementations from the **same** assembly automatically.
So the two fix providers are new files inside the existing `ProjectCeres.Analyzers/`
project; the IDE picks them up with **zero** changes to how the main app references
anything.

### 2.2 The one dependency change

| File | Change |
|---|---|
| `ProjectCeres.Analyzers/ProjectCeres.Analyzers.csproj` | Add `<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.11.0" PrivateAssets="all" />` |

`Microsoft.CodeAnalysis.CSharp.Workspaces` is where the `CodeFixProvider` base class,
`CodeAction`, and the syntax-rewrite helpers live. Version matched to the analyzer's
existing `Microsoft.CodeAnalysis.CSharp` 4.11.0 reference. `PrivateAssets="all"` matches
the existing analyzer-reference hygiene and keeps the package out of the downstream
runtime/test closure.

### 2.3 New source files

| File | Responsibility |
|---|---|
| `ProjectCeres.Analyzers/PreAuthScopeTransactionCodeFixProvider.cs` | The CER001 placeholder fix |
| `ProjectCeres.Analyzers/DateTimeWallClockCodeFixProvider.cs` | The CER004 safe fix |

One fix, one class, one file — matching the existing one-analyzer-per-file layout. **No
shared base class:** two providers do not justify the abstraction, and they share almost no
logic (one gates on a field lookup, the other does not).

### 2.4 What does NOT change

- **No `AnalyzerReleases` row.** That tracking file is keyed to *diagnostic descriptors*;
  a code-fix provider emits no new diagnostic → no row → no RS2008 concern. (Same as 9.5i.)
- **No soak / no warning→error flip.** Code-fix providers are not severities; nothing to
  flip. This stage closes `[x]` in one sitting once it ships, like 9.5i.
- **No change to the analyzers themselves.** CER001 and CER004 fire exactly as today; the
  fix providers consume their diagnostic IDs.

---

## 3. Internal behavior of each fix

Both providers follow the same Roslyn skeleton (confirmed against the official
`dotnet/roslyn` CodeFixProvider docs):

- `[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(<Provider>)), Shared]`
- override `FixableDiagnosticIds` → the single diagnostic ID
- override `GetFixAllProvider()` → `WellKnownFixAllProviders.BatchFixer` (free "fix all
  occurrences" support)
- override `RegisterCodeFixesAsync(CodeFixContext)` → find target node via
  `root.FindNode(diagnostic.Location.SourceSpan)`, register a `CodeAction` whose
  `createChangedDocument` does a single `root.ReplaceNode(old, new)` and returns the updated
  `Document`

They differ only in the **gate** and the **replacement node**.

### 3.1 CER004 — `DateTimeWallClockCodeFixProvider`

1. From the diagnostic location, locate the flagged `MemberAccessExpressionSyntax`
   (`DateTime.UtcNow` or `DateTime.Now`).
2. **Gate (the safety guarantee):** get the enclosing `TypeDeclarationSyntax`, resolve its
   symbol, and look for an **instance field** whose name is exactly `_timeProvider` and
   whose type is `System.TimeProvider` or a subtype (resolved via the semantic model, so a
   derived test clock still counts). If no such field → **register no fix** and return. The
   fix only appears when it provably compiles.
3. **Replacement:** build the member-access chain
   `_timeProvider.GetUtcNow().UtcDateTime` and `ReplaceNode` the original.
   Code action title: `Use _timeProvider.GetUtcNow().UtcDateTime`.

### 3.2 CER001 — `PreAuthScopeTransactionCodeFixProvider`

1. From the diagnostic location, locate the flagged `InvocationExpressionSyntax`
   (`….BeginTransactionAsync()`).
2. **No gate** — the fix is always offered (placeholder posture).
3. **Replacement (method name + args only; receiver left as-is):** on the invocation's
   `MemberAccessExpressionSyntax`, replace the method name identifier
   `BeginTransactionAsync` → `BeginPreAuthUserScopeAsync`, and replace the empty argument
   list with two bare `IdentifierName` arguments, `userId` and `ct`, that resolve to
   nothing. **The receiver is not touched** — `_db.Database` stays `_db.Database`. Result:
   `await _db.Database.BeginPreAuthUserScopeAsync(userId, ct)`.
   Code action title: `Use BeginPreAuthUserScopeAsync(userId, ct)`.

   **Receiver rationale (decided 2026-06-10):** the correct helper hangs off `_db`, not
   `_db.Database`, so the rewritten call also fails to resolve the method on `Database`
   (`CS1061`). That error is **intentional** — it joins the two `CS0103` errors as part of
   the same to-do list. The fix makes **zero** assumptions about call-site receiver shape
   (a future caller shaped differently is not mis-rewritten), and the build stays red
   regardless (placeholder posture), so leaving the receiver is both safer and more honest
   than guessing it should be `_db`.

---

## 4. Testing

### 4.1 New harness

`ProjectCeres.Analyzers.Tests/TestHelpers/CSharpCodeFixVerifier.cs`, mirroring the existing
`CSharpAnalyzerVerifier`. Wraps
`CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>` and exposes
`VerifyCodeFixAsync(string source, string fixedSource, params DiagnosticResult[] expected)`.
`ReferenceAssemblies = ReferenceAssemblies.Net.Net100` (same as the analyzer verifier).

The code-fix testing types ship in `Microsoft.CodeAnalysis.CSharp.CodeFix.Testing` — same
`1.1.x` family as the `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` and
`…SourceGenerators.Testing` packages already in `ProjectCeres.Analyzers.Tests.csproj`. The
test project already references `Microsoft.CodeAnalysis.CSharp.Workspaces` 4.11.0. The
implementer adds the CodeFix.Testing package and confirms it resolves **empirically at first
build** rather than guessing the exact package id/version (the 9.5i "run and read, don't
guess" discipline that caught the CS0117 surprise).

### 4.2 `CER004_DateTimeWallClockCodeFixTests.cs`

- **Fixes when `_timeProvider` field present.** `TestCode`: a service class with a
  `private readonly System.TimeProvider _timeProvider;` field and a body using
  `System.DateTime.UtcNow`. `FixedCode`: same with `_timeProvider.GetUtcNow().UtcDateTime`.
  No residual diagnostics.
- **Offers no fix when field absent (negative assertion — load-bearing).** `TestCode`: same
  body, no `_timeProvider` field. Assert the fix leaves the source unchanged
  (`FixedCode == TestCode`), proving the gate suppresses the action. This pins what the fix
  does **not** do — the entire CER004 safety guarantee.
- **Fixes through a TimeProvider subtype field.** `TestCode`: the field is typed as a
  class deriving from `System.TimeProvider`. The fix still fires (semantic-model type check,
  not a string match on `"TimeProvider"`).

### 4.3 `CER001_PreAuthScopeTransactionCodeFixTests.cs`

- **Placeholder fix leaves the three expected errors.** `TestCode`: a `[PreAuthScope]`-
  marked class calling `_db.Database.BeginTransactionAsync()`. `FixedCode`:
  `_db.Database.BeginPreAuthUserScopeAsync(userId, ct)`. `FixedState.ExpectedDiagnostics`
  pins the two `CS0103` (`userId`, `ct`) **plus** the receiver error (`CS1061` —
  `Database` has no such method) — the deliberate to-do list. This test proves the
  placeholder posture is intentional, not a bug.

(Exact compiler-error codes — `CS0103`, `CS1061` — are the expected shapes; the implementer
confirms them empirically from the first test run per the 9.5i discipline and adjusts the
`ExpectedDiagnostics` to whatever the harness actually reports, without weakening the
assertion.)

### 4.4 Test discipline

- The "offers no fix when field absent" case is the negative-assertion ship-gate
  (`feedback_test_edge_cases_as_ship_gate`): it pins the safety boundary, not a happy path.
- These are analyzer-test-project tests; the implementer runs
  `dotnet test ProjectCeres.Analyzers.Tests` per task. The full suite runs via the Stop
  hook's tiering.

---

## 5. Stage close-out

- No `AnalyzerReleases` change, no soak, no flip → this stage closes `[x]` in one sitting
  once both providers ship and the analyzer-test suite is green.
- Doc sync on close (the four-place re-scope record for CER010 + the two-fix delivery):
  ADR-0077 (note the family gained code-fix providers + the CER010 re-scope),
  `docs/roadmap-phase-three.md` (rewrite the 9.5j line, tick `[x]`),
  `docs/testing.md` (the code-fix test harness + the three test cases),
  `CHANGELOG.md` (Analyzers group under [Unreleased]→Phase 3).
- After 9.5j closes, the 9.5h batch close-out remains blocked only on the CER005 + CER006
  warning→error flips (both eligible ~2026-06-11 after their ≥48h C-2 soak).

---

## 6. Decisions log (this brainstorm)

1. **Fix posture: hybrid** — safe where provable, placeholder where named. (CER004 safe;
   CER001 placeholder; CER010 none.)
2. **CER004 detection:** instance field named exactly `_timeProvider` of type
   `System.TimeProvider`/subtype; no field → no fix. (Matches the project's uniform clock
   convention; the gentlest blind-spot failure mode — warning simply stays.)
3. **CER001 placeholder:** named identifiers `userId, ct` (undeclared → `CS0103`), not
   `/* TODO */` comments (syntax error) and not `default, default` (compiles → ships broken
   user scope).
4. **CER001 receiver:** swap method + args only, leave `_db.Database` as-is (zero call-site
   assumption; the receiver error joins the same to-do list).
5. **Package:** add `Microsoft.CodeAnalysis.CSharp.Workspaces` 4.11.0 `PrivateAssets=all`
   to the analyzer project in-place; no separate CodeFixes project (4 wiring points of
   overhead for 2 fix classes).
