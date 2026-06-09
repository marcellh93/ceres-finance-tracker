# Stage 9.5g — CER006 reverse-direction [PreAuthScope] marker analyzer (design)

**Date:** 2026-06-09
**Stage:** 9.5g (sub-stage of 9.5h, the Phase 1 hardening container)
**Status:** design — approved in brainstorm, pending implementation plan
**ADR:** [ADR-0077](../../decisions/ADR-0077-roslyn-analyzers-for-invariant-enforcement.md) (analyzer family)
**Prior art:** Stage 9.5f spec (`2026-06-09-stage-9-5f-cer005-tokenlookup-analyzer-design.md`); Stage 9.5c spec (CER001 — the analyzer this one mirrors)

---

## 1. Problem

The `[PreAuthScope]` marker attribute labels a class as operating on the **pre-authentication code path** — a class that legitimately opens a per-request user-scoped database transaction (via `BeginPreAuthUserScopeAsync`) before the user is logged in, so writes to RLS-protected user-owned tables land under the correct user context. The marker is not decoration: it is the documented contract that other tooling and reviewers key off to answer "which classes touch the pre-auth path."

Stage 9.5c shipped **CER001**, which enforces this contract in one direction: *a class marked `[PreAuthScope]` must use `BeginPreAuthUserScopeAsync`, not a raw `BeginTransactionAsync`.* But the reverse hole is open: a class can **call `BeginPreAuthUserScopeAsync` without carrying the `[PreAuthScope]` marker**. Such a class is doing pre-auth work but is invisible to anything that audits the pre-auth surface — a silent gap.

CER006 closes that reverse direction: *a class that calls `BeginPreAuthUserScopeAsync` must carry the `[PreAuthScope]` marker.* CER001 and CER006 together pin the contract from both sides — you cannot have the marker without the helper (CER001), and you cannot have the helper without the marker (CER006).

### 1.1 Why now, and why an analyzer

Per the L5 rule (ADR-0077): a syntactically-locatable invariant that has a clear enforcement shape migrates to a Roslyn analyzer. CER006's invariant — "this call site's enclosing class carries this attribute" — is locatable on the call's syntax tree, exactly CER001's proven shape. 9.5g was reserved for it at the 9.5c lock; this is that sub-stage. ADR-0077 §Consequences already names it: "CER006 (reverse-direction marker, planned for Stage 9.5g)."

---

## 2. Design — a near-exact mirror of CER001

CER006 is CER001's machinery with **three deltas and nothing else**. CER001 (verified at `PreAuthScopeTransactionAnalyzer.cs`): registers on invocation expressions, matches a target method name, walks up to the enclosing type declaration, reads the type's `[PreAuthScope]` attribute, and reports. CER006 keeps that walk verbatim and changes only:

| Aspect | CER001 (exists) | CER006 (this) |
|---|---|---|
| Target method name | `BeginTransactionAsync` | `BeginPreAuthUserScopeAsync` |
| Fire condition | marker **present** AND raw transaction called | helper called AND marker **absent** |
| Helper-file path exclusion | yes (`PreAuthRlsScope.cs`) | **no** (see §2.2) |

Everything else — `RegisterSyntaxNodeAction(SyntaxKind.InvocationExpression)`, `FirstAncestorOrSelf<TypeDeclarationSyntax>()`, `GetDeclaredSymbol`, `GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "ProjectCeres.Analyzers.Annotations.PreAuthScopeAttribute")`, the report location and message-argument shape — is identical.

### 2.1 CER006 does NOT inherit the CER002 `GetEnclosingSymbol` gotcha

ADR-0077 §Consequences notes that **CER002**'s containing-scope check uses `GetEnclosingSymbol as IMethodSymbol`, which returns null for property getters/setters, lambdas, and local functions — so CER002 silently exits there, and "CER006 is expected to surface this gap." That framing is easy to misread as "CER006 inherits the gotcha." It does not. CER006 mirrors **CER001**, and CER001 uses a **syntax-tree** walk (`invocation.FirstAncestorOrSelf<TypeDeclarationSyntax>()` at `PreAuthScopeTransactionAnalyzer.cs:33`), which walks *lexical* scope, not the *semantic* `GetEnclosingSymbol` chain. A call inside a lambda or a property getter still lexically sits inside a `TypeDeclarationSyntax`, so `FirstAncestorOrSelf` finds the enclosing class correctly. CER006 fires correctly in exactly the spots CER002 can't. (Verified during the verify-against-codebase audit.)

### 2.2 Scope is the enclosing class, and no exclusions are needed

**Class, not method.** `[PreAuthScope]` is declared `AttributeTargets.Class` (verified at `PreAuthScopeAttribute.cs:5`, `AllowMultiple = false, Inherited = false`). So CER006 checks the enclosing **class** for the marker — a marked class may call the helper from any of its methods. It never checks methods (the attribute can't sit there).

**No helper-file exclusion.** CER001 excludes `PreAuthRlsScope.cs` because that file's `BeginPreAuthUserScopeAsync` implementation legitimately calls `BeginTransactionAsync` internally — the exact thing CER001 flags. CER006 targets calls to `BeginPreAuthUserScopeAsync`; `PreAuthRlsScope.cs` only **defines** that method (`PreAuthRlsScope.cs:71`) and never **calls** it (the one mention at line 43 is inside a `///` doc-comment, which the analyzer never sees as code). So CER006 would never fire there, and an exclusion would be dead code — worse, it could *hide* a genuine future bug if the helper file ever did call the helper unmarked. Omitted deliberately.

**No test-project exclusion.** The analyzer is wired into `ProjectCeres.csproj` with `PrivateAssets="all"` (verified at `ProjectCeres.csproj:30-34`), which stops it from flowing transitively into projects that reference `ProjectCeres`. `ProjectCeres.Tests` references `ProjectCeres` but not `ProjectCeres.Analyzers`, so CER006 never runs on test-project source. The five test files that call `BeginPreAuthUserScopeAsync` are invisible to it by construction — no path/namespace guard needed. This is the established mechanism for every existing CER analyzer (none has test-exclusion logic).

**Net result:** CER006 needs **zero exclusion logic** — strictly simpler than CER001 (which carries one path exclusion).

---

## 3. Result against today's `main` — zero violations

All production callers of `BeginPreAuthUserScopeAsync` carry `[PreAuthScope]` on the class declaration (verified, with call-site lines):

| Class | `[PreAuthScope]` on class | Calls helper at |
|---|---|---|
| `AuditLogWriter` | `:9` | `:61` |
| `MfaBackupCodeService` | `:12` | `:85` |
| `LockoutUnlockService` | `:24` | `:98`, `:184` |
| `EmailChangeService` | `:20` | `:280`, `:437` |
| `PasswordResetService` | `:18` | `:155`, `:299` |
| `EmailConfirmationService` | `:19` | `:103`, `:260` |
| `TotpReplayGuard` | `:10` | `:68` |
| `AuthController` | `:21` | `:101` |

**8 callers, all marked → 0 CER006 violations on `main`** — the precondition for the C-2 soak. (The roadmap line says "7 current callers"; the live count is **8**. Corrected in the roadmap update, the same kind of stale-count fix CER005's spec made for "3 token tables" → 4.)

**What CER006 catches — future drift.** A new pre-auth service (say `PhoneVerificationService`) that calls `BeginPreAuthUserScopeAsync` but omits `[PreAuthScope]` fires CER006 at build time, on the call site, naming the class.

---

## 4. Severity lifecycle — the C-2 soak

Identical to CER005. Ships at `DiagnosticSeverity.Warning`; the flip to build-failing `error` is a separate one-line `.editorconfig` change (`dotnet_diagnostic.CER006.severity = error`), gated on **≥48 calendar hours since ship AND zero violations**. Zero is already satisfied (§3), so the flip is gated only on the soak. The `.editorconfig` CER block (lines 16–20) is the flip target; shipping at `warning` requires **no** `.editorconfig` change.

**9.5g closes in two sittings**, same as 9.5f: analyzer + tests + roadmap `[ ]` now at `warning`; the flip returns ≥48 h later and ticks the line `[x]`. The flip is tracked as a dated follow-up (§8). (CER006's flip can ride alongside CER005's, since both become soak-eligible around the same date.)

---

## 5. Release tracking — RS2008 (build-breaking)

The descriptor commit **must** add this row to `ProjectCeres.Analyzers/AnalyzerReleases.Unshipped.md` under `### New Rules` (after the CER005 row), or RS2008 fails the build:

```
CER006 | Reliability | Warning | A class that calls BeginPreAuthUserScopeAsync must carry the [PreAuthScope] marker
```

- **Category `Reliability`** — matches CER001 (same invariant family); must equal the descriptor's `category`.
- **Severity `Warning`** — initial ship value; never changes, even after the `.editorconfig` flip to error.
- **No trailing period** (RS1032); **no column padding** (RS2007) — single-space pipes, plain `--------|...` separator.

---

## 6. Files and tests

### 6.1 Files touched

| File | Change | Commit |
|---|---|---|
| `ProjectCeres.Analyzers/Diagnostics.cs` | add `CER006_PreAuthScopeMarkerMissing` descriptor | Ship |
| `ProjectCeres.Analyzers/PreAuthScopeMarkerAnalyzer.cs` | new analyzer (~45 lines, mirrors CER001) | Ship |
| `ProjectCeres.Analyzers/AnalyzerReleases.Unshipped.md` | add the CER006 row (§5) | Ship |
| `ProjectCeres.Analyzers.Tests/CER006_PreAuthScopeMarkerAnalyzerTests.cs` | the 4 tests (§6.2) | Ship |
| `.editorconfig` | `dotnet_diagnostic.CER006.severity = error` | **Flip** (≥48 h later) |

No change to `ProjectCeres.csproj` (analyzer wiring already covers the whole assembly), no Annotations-project change (the `[PreAuthScope]` attribute already ships from 9.5c), no model/HTTP/Razor change.

### 6.2 Test matrix (ship-gates, TDD — written first)

Test file `CER006_PreAuthScopeMarkerAnalyzerTests.cs`, harness `CSharpAnalyzerVerifier<PreAuthScopeMarkerAnalyzer>`. Each source inline-defines a stub `PreAuthScopeAttribute` (in namespace `ProjectCeres.Analyzers.Annotations`, matched by full display-string name) and a stub `BeginPreAuthUserScopeAsync` extension method so the source compiles — the exact stub shape CER001's tests already use. The `{|#0:...|}` marker goes on the **call expression** (CER001 reports at `invocation.GetLocation()`, so the marker sits on the call, not the class — note this differs from CER005, which reported on the class identifier).

| # | Source shape | Expect |
|---|---|---|
| 1 | unmarked class calls `BeginPreAuthUserScopeAsync` | **fires** (the drift case) |
| 2 | `[PreAuthScope]` class calls `BeginPreAuthUserScopeAsync` | no fire (the 8 real callers' shape) |
| 3 | unmarked class, no call to the helper | no fire (only callers checked) |
| 4 | unmarked class calls some **other** method (not the helper) | no fire (only `BeginPreAuthUserScopeAsync` triggers it) |

Cases 1–3 are the roadmap's required set; case 4 is an added negative-assertion pinning "only the helper method triggers it, not any call" (per `feedback_test_edge_cases_as_ship_gate`). The message argument is the enclosing class name, asserted via `.WithArguments("...")`.

### 6.3 Commit shape (mirrors CER005)

| Commit | Contents | Severity |
|---|---|---|
| **Ship** | descriptor + analyzer + release row + 4 tests | `Warning` |
| **Flip** (≥48 h, 0 violations) | one `.editorconfig` line | `Error` |
| **Doc-sync / close-out** | roadmap `[x]`, changelog, ADR-0077 row | — |

---

## 7. Inverted-TDD ordering (same as CER005)

A Roslyn analyzer's test project must compile against the descriptor symbol, so the order is: **ship the descriptor + release row + an inert analyzer first** (compiles, RS2008-clean, reports nothing) → **write the 4 tests** (the 1 fires-case goes Red, the 3 no-fire cases pass) → **implement the mirror gate** (Red → Green). This worked cleanly for CER005; CER006 reuses it.

---

## 8. Deferred work (durable, with receiving destinations)

| Item | Destination | Tripwire |
|---|---|---|
| **warning→error flip** (≥48 h after ship, 0 violations) | roadmap 9.5g line stays `[ ]` for the flip half until done; tracked here | `.editorconfig` line absent = flip not done |
| **stale "7 callers" count in roadmap 9.5g line** | corrected to 8 in the same commit that ships this spec's roadmap update | — |

No other deferrals.

---

## 9. Open questions

None. All forks resolved in brainstorm:
1. Helper-file exclusion → **none** (helper only defines, never calls).
2. Test-project scope → **production-only, for free** (`PrivateAssets=all`; no code needed).
3. Class-vs-method scope → **class** (forced by `AttributeTargets.Class`).
