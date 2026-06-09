# Stage 9.5f — CER005 HMAC TokenLookup discipline analyzer (design)

**Date:** 2026-06-09
**Stage:** 9.5f (sub-stage of 9.5h, the Phase 1 hardening container)
**Status:** design — approved in brainstorm, pending implementation plan
**ADR:** [ADR-0077](../../decisions/ADR-0077-roslyn-analyzers-for-invariant-enforcement.md) (analyzer family)
**Prior art:** Stage 9.5c spec (`2026-05-26-stage-9-5c-roslyn-analyzers-design.md`) — shipped CER001/002/004/010/020

---

## 1. Problem

Token entities — rows representing one issued single-use secret link — must carry a `TokenLookup` column: a fixed-width HMAC fingerprint of the raw token, uniquely indexed, so a `/confirm` request locates its row in one indexed hop instead of running an Argon2id verify against every candidate row. Without it, the verify path is O(N) Argon2id, and under accumulated token load it saturates CPU.

This is not hypothetical. Stage 9.1.5.a shipped `LockoutUnlockToken` **without** `TokenLookup`; its `/lockout-unlock` confirm path ran Argon2id over every unconsumed-unexpired row, saturating the integration-test workers and producing a ~15-minute suite-runtime regression plus timing-flakes on adjacent rate-limit tests. The fix retrofitted the Stage 6.15 `TokenLookup` pattern. The class-of-bug is: **a new `*Token` entity ships, the author wires up everything else, but forgets the lookup fingerprint.**

CER005 is the compile-time guard against that recurrence: a Roslyn analyzer that fails the build (after a soak) when a user-owned token entity lacks its `TokenLookup` property.

### 1.1 Why an analyzer, and why now

Per the L5 rule (ADR-0077, 9.5e §5.4): any invariant that is **syntactically locatable** and has fired as a real bug migrates from prose/review enforcement to a Roslyn analyzer or pre-commit hook. The TokenLookup-presence invariant is syntactically locatable on a single class declaration — exactly the analyzer's sweet spot. 9.5f was deliberately excluded from the 9.5c batch (locked at four analyzers per user Q3, 2026-05-26: "ship CER005 candidates as separate sub-stages later"); this is that sub-stage.

---

## 2. Scope decision — property presence only

CER005 checks **one** thing: that a user-owned token class declares a `byte[] TokenLookup` property. It does **not** verify the matching unique index in `AppDbContext.OnModelCreating`, and does **not** scan the migrations for the `CreateIndex`.

**Why not the index/migration:**

- A Roslyn analyzer reasons over one compilation's symbols and syntax. The unique index lives in `AppDbContext.OnModelCreating` as an imperative lambda (`b.HasIndex(e => e.TokenLookup).IsUnique()`) inside a per-entity config method; matching "this token class" → "a `HasIndex(e => e.TokenLookup).IsUnique()` call for *this* entity, somewhere in another file" is cross-file syntactic reconciliation that breaks the moment config is factored into a helper, a loop, or a shared base method. It would false-negative silently — the worst failure mode for a guard.
- The model↔migration coupling is **already** enforced, at build time, by a green mechanism that shipped last stage: `MigrationDriftTests.Model_has_no_pending_migration_changes` (Condition E3, 9.5e), which calls EF's `HasPendingModelChanges()`. Add a `TokenLookup` property and forget the migration → that test fails the build. Add the index in `OnModelCreating` and forget to migrate → same. CER005 chasing the migration would near-totally overlap an existing green check.
- A unique-index violation at runtime throws on the first duplicate insert; it cannot ship silently the way a *missing property* can.
- Every existing CER analyzer checks **one locatable syntactic fact**, never cross-file schema reconciliation. CER005 stays in that mold (single-class, same-file read), which keeps it robust and cheap.

**The gap this leaves, and who covers it:**

| Mistake | Caught by |
|---|---|
| `*Token` entity with no `TokenLookup` property | **CER005** (this spec) |
| `TokenLookup` property exists but no migration | E3 `MigrationDriftTests` (shipped 9.5e) |
| `TokenLookup` property exists, no unique index in `OnModelCreating` | runtime (duplicate insert throws) + E3 if it changes the model snapshot |
| `TokenLookup` typed wrong (`string`/`int`) | **CER005** (type is part of the check — see §3) |

---

## 3. The detection gate

CER005 registers on **named types** and, for each, asks four questions. It fires if and only if the first three are **yes** and the fourth is **no**:

| # | Question | Symbol-level check |
|---|---|---|
| 1 | Is it a class named `*Token`? | `symbol.TypeKind == TypeKind.Class` **and** `symbol.Name.EndsWith("Token", Ordinal)` |
| 2 | Is it in the models namespace? | `symbol.ContainingNamespace.ToDisplayString().StartsWith("ProjectCeres.Models", Ordinal)` |
| 3 | Is it a user-owned row? | `symbol.AllInterfaces.Any(i => i.ToDisplayString() == "ProjectCeres.Common.IUserOwned")` |
| 4 | Does it carry the fingerprint? | `symbol.GetMembers().OfType<IPropertySymbol>().Any(p => p.Name == "TokenLookup" && p.Type is IArrayTypeSymbol { ElementType.SpecialType: System_Byte })` |

**Diagnostic:** fires on the class declaration's location, message names the missing property:
> `Token entity '{0}' must declare a 'byte[] TokenLookup' property (HMAC fingerprint with a unique index) so confirm paths look up in O(1) instead of running Argon2id over every candidate`

(No trailing period — RS1032.)

### 3.1 Why `TypeKind.Class` is load-bearing, not cosmetic

`RegisterSymbolAction(..., SymbolKind.NamedType)` hands the callback **every** named type, including enums and structs. `ProjectCeres/Models/EmailChangeToken.cs` declares `public enum EmailChangeTokenPurpose` in the `ProjectCeres.Models` namespace. The four gate questions are ANDed, so this enum is already excluded twice over — by question 1's `EndsWith("Token")` (its name ends `...TokenPurpose`) **and** by question 1's `TypeKind == TypeKind.Class` (an enum is not a class). The `TypeKind.Class` clause is the load-bearing one: it guarantees no enum or struct named exactly `*Token` (a future `enum SessionToken`, say) is ever examined for the `IUserOwned`/property questions. Both clauses live inside question 1; the implementation checks `TypeKind.Class` before `EndsWith` so the cheaper test short-circuits first.

### 3.2 Why `IUserOwned` is the gate, not name alone

The roadmap phrasing is "classes named `*Token` under `ProjectCeres/Models/`," but name-alone would fire on any future non-entity class (a DTO, a value object) that wanders into `Models/` with a token-ish name, forcing the author to either add a meaningless `TokenLookup` or suppress the rule. Every **real** token row implements `IUserOwned` (the marker saying "this row belongs to a user") — verified across all four entities. Gating on `IUserOwned` makes the rule precise: it polices database token rows, nothing else. House precedent: `IgnoreQueryFiltersOnUserOwnedAnalyzer` (CER002) already uses `INamedTypeSymbol.AllInterfaces` to test `IUserOwned` membership — CER005 reuses that idiom.

### 3.3 Why the type must be `byte[]`

All four entities declare `public byte[] TokenLookup` (32-byte HMAC-SHA256, DB-indexed as binary). A `string` or `int` lookup column would technically satisfy a name-only check but silently break the indexing/uniqueness contract the pattern relies on. Checking `IArrayTypeSymbol` with `byte` element type closes that hole and lets the message say `byte[] TokenLookup` precisely.

---

## 4. Result against today's `main` — zero violations

Walked against live source (verified during brainstorm):

| Class | `class` & `*Token` | `Models` ns | `IUserOwned` | `byte[] TokenLookup` | Fires? |
|---|---|---|---|---|---|
| `PasswordResetToken` | yes | yes | yes | yes | no ✓ |
| `EmailChangeToken` | yes | yes | yes | yes | no ✓ |
| `EmailConfirmationToken` | yes | yes | yes | yes | no ✓ |
| `LockoutUnlockToken` | yes | yes | yes | yes | no ✓ |
| `EmailChangeTokenPurpose` (enum) | not a class | — | — | — | no ✓ (q1) |
| `*TokenGenerator`, `TokenLookupHasher`, `TokenLookupOptions`, `PersistentTokenService` | (not in `Models/`) | no | — | — | no ✓ (q2) |
| `InvalidToken` outcome records | (not in `Models/`) | no | no | — | no ✓ (q2/q3) |

**0 violations on `main`** — required for the C-2 soak (no baseline suppressions needed). The roadmap line's "existing 3 token tables" is stale (predates `EmailConfirmationToken`, added Stage 9.3); the live count is **4**, and the spec/roadmap are corrected to 4.

---

## 5. Severity lifecycle — the C-2 soak

Per ADR-0077, every new CER rule ships at `warning`, soaks, then flips to `error`:

| Step | Change | Gate |
|---|---|---|
| **Ship** | CER005 added at `DiagnosticSeverity.Warning` | this stage's descriptor commit |
| **Flip** | one `.editorconfig` line → `error` | **both** clocks green: ≥48 calendar hours since ship **and** 0 outstanding violations |

The "0 violations" clock is already satisfied (§4), so the flip is gated only on the 48-hour wall-clock soak. The flip is a separate near-empty commit adding `dotnet_diagnostic.CER005.severity = error` to the repo-root `.editorconfig` beside the CER001/002/004/010 block (lines 16–19). This mirrors 9.5c's flip (`665f182`, 2026-05-30).

**Consequence: 9.5f closes in two sittings.** The analyzer + tests + roadmap `[x]` ship now at `warning`; the warning→error flip returns ≥48 h later. The flip is tracked as a dated follow-up (§8) so it is not lost.

---

## 6. Release tracking — RS2008 (build-breaking)

The Roslyn meta-analyzer **RS2008** fails the analyzer build if a `DiagnosticDescriptor` ID is absent from `AnalyzerReleases.{Shipped,Unshipped}.md`. The descriptor commit **must** add this row to `ProjectCeres.Analyzers/AnalyzerReleases.Unshipped.md` under the existing `### New Rules` table:

```
CER005 | Reliability | Warning | *Token classes under ProjectCeres/Models/ implementing IUserOwned must declare a byte[] TokenLookup property
```

Format rules (each is a separate build-breaking meta-analyzer if violated):
- **Category `Reliability`** — same bucket as CER001/CER004 (a correctness/performance invariant, not Security or Localization). Must match the descriptor's `category`.
- **Severity column `Warning`** — records *initial ship* severity; never changes, even after the `.editorconfig` flip to error (the four behavioral siblings all still read `Warning` here).
- **No trailing period** in the Notes text (RS1032).
- **No column padding** — plain single-space-delimited pipes, plain `--------|...` separator (RS2007).

---

## 7. Files and tests

### 7.1 Files touched

| File | Change | Commit |
|---|---|---|
| `ProjectCeres.Analyzers/Diagnostics.cs` | add `CER005_TokenLookupDiscipline` descriptor | Ship |
| `ProjectCeres.Analyzers/TokenLookupDisciplineAnalyzer.cs` | new analyzer (~50 lines), `RegisterSymbolAction(SymbolKind.NamedType)` | Ship |
| `ProjectCeres.Analyzers/AnalyzerReleases.Unshipped.md` | add the CER005 row (§6) | Ship |
| `ProjectCeres.Analyzers.Tests/CER005_TokenLookupAnalyzerTests.cs` | the 7 tests (§7.2) | Ship |
| `.editorconfig` | `dotnet_diagnostic.CER005.severity = error` | **Flip** (≥48 h later) |

No change to `ProjectCeres.csproj` (the `OutputItemType="Analyzer"` wiring already covers the whole analyzer assembly), no new annotation attribute, no model changes.

### 7.2 Test matrix (all ship-gates, TDD — written first)

Test file `CER005_TokenLookupAnalyzerTests.cs`, harness `CSharpAnalyzerVerifier<TokenLookupDisciplineAnalyzer>`. Each source inline-defines a stub `ProjectCeres.Common.IUserOwned` interface in its preamble and places the token type in `ProjectCeres.Models`, so the analyzer's full-name + namespace checks resolve (the same preamble idiom CER004's tests use). The `{|#0:...|}` marker goes on the **class declaration**.

| # | Source shape | Expect |
|---|---|---|
| 1 | `class FooToken : IUserOwned` in `Models`, **no** `TokenLookup` | **fires** (core regression) |
| 2 | `class FooToken : IUserOwned` in `Models` + `byte[] TokenLookup` | no fire (real-entity shape) |
| 3 | `class FooToken : IUserOwned` in `Models` + **`string` `TokenLookup`** | **fires** (wrong type — pins §3.3) |
| 4 | `class FooToken` in `Models`, **no** `IUserOwned`, no `TokenLookup` | no fire (non-entity helper — pins §3.2) |
| 5 | `class Account : IUserOwned` in `Models` (non-`*Token`), no `TokenLookup` | no fire (only token classes policed — pins q1) |
| 6 | `class FooToken : IUserOwned` **outside** `Models` ns, no `TokenLookup` | no fire (namespace gate — pins q2) |
| 7 | `enum FooToken` in `Models` | no fire (only classes examined — pins §3.1) |

Cases 1 & 3 prove it catches; 2, 4–7 each pin one exclusion against future drift. This satisfies `feedback_test_edge_cases_as_ship_gate` (negative-assertion test per exclusion branch).

### 7.3 Commit shape (mirrors 9.5c, scaled to one analyzer)

| Commit | Contents | Severity |
|---|---|---|
| **Ship** | descriptor + analyzer + release-tracking row + 7 tests | `Warning` |
| **Flip** (≥48 h, 0 violations) | one `.editorconfig` line | `Error` |
| **Doc-sync / close-out** | roadmap `[x]`, changelog, ADR-0077 note if needed | — |

---

## 8. Deferred work (durable, with receiving destinations)

| Item | Destination | Tripwire |
|---|---|---|
| **warning→error flip** (≥48 h after ship, 0 violations) | roadmap 9.5f line stays `[ ]` for the flip half until done; tracked here | `.editorconfig` line absent = flip not done; `dotnet build` at `warning` still green either way |
| **stale "3 token tables" count in roadmap 9.5f line** | corrected to 4 in the same commit that ships this spec's roadmap update | — |

No other deferrals. The index/migration coverage is **not** deferred — it is owned by E3 (§2), already green.

---

## 9. Open questions

None. All three design forks were resolved in brainstorm:
1. Check scope → property-only (index/migration owned by E3).
2. Trigger gate → name `*Token` + `IUserOwned` (+ `TypeKind.Class`).
3. Property check → name + `byte[]` type.
