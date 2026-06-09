# Stage 9.5i — Typed email-key source generator (design)

**Date:** 2026-06-09
**Stage:** 9.5i (sub-stage of 9.5h, the Phase 1 hardening container)
**Status:** design — approved in brainstorm, pending implementation plan
**ADR:** [ADR-0077](../../decisions/ADR-0077-roslyn-analyzers-for-invariant-enforcement.md) (analyzer/generator family)
**Prior art:** Stage 9.5c CER020 (`ResxParityGenerator`) — the only existing `IIncrementalGenerator`, whose read-resx pipeline this mirrors

---

## 1. Problem — and the re-scope the audit forced

**Roadmap premise (falsified):** the 9.5i roadmap line assumes a sprawl of hand-typed string keys (`_localizer["Auth.Register.Title"]`) that a typed accessor would replace, "migrating consumers from string keys." **That code does not exist.** The verify-against-codebase audit confirmed:

- **Zero** static-literal `_localizer["..."]` call sites anywhere in the project.
- **One** direct consumer of `IStringLocalizer<EmailsResource>`: `EmailComposer.Compose`, which builds keys *dynamically* — `_localizer[$"{key}.Subject"]`, `.BodyText`, `.BodyHtml` — where `key` is an `EmailTemplateKey` enum value.
- The 10 enum values **exactly match** the 10 resx key prefixes (verified 1:1). The 30 resx keys are `{EnumValue}.{Subject|BodyText|BodyHtml}`.
- The 4 auth services are *indirect* — they call `IEmailComposer.Compose(enum, …)`, never the localizer.

So there are no string-key consumers to migrate. The real silent-breakage risk here is **drift between the resx and the code**: rename `PasswordResetRequest.Subject` in the resx and `Compose` silently falls back at *runtime* (the missing-key fallback returns the key string). That is the exact class of bug the roadmap wanted to make a compile error — it just manifests through the dynamic-key path, not string literals.

**9.5i, re-scoped (decided in brainstorm):** a source generator emits a named C# constant per resx key, and `EmailComposer.Compose` is refactored to reference those constants instead of interpolating keys — so a resx-key rename becomes a **compile error**. CER020 (EN/ES parity) stays untouched; 9.5i is additive.

### 1.1 Why a generator, and why this honors the roadmap

The roadmap's literal asks — "typed accessor source generator … typos compile errors … migrate all consumers in one commit" — are all satisfied by the re-scope: the generator emits typed members, a rename is a compile error, and `EmailComposer` (the one real consumer) migrates in the same commit. The only thing that changes is *what* "typed accessor" means, because the consumer turned out to be dynamic-keyed rather than string-literal-keyed.

---

## 2. The two pieces

### 2.1 The generator — `EmailKeysGenerator`

A new `[Generator(LanguageNames.CSharp)] IIncrementalGenerator` in `ProjectCeres.Analyzers/`, mirroring CER020's pipeline (read resx via `AdditionalTextsProvider` → parse `<data name>` via `XDocument` → `spc.AddSource`). It:

1. Filters additional files to the **email resource specifically**: `context.AdditionalTextsProvider.Where(f => f.Path.EndsWith("EmailsResource.en.resx", StringComparison.OrdinalIgnoreCase))` — the `.en` culture only (CER020 already guarantees EN/ES parity, so reading one culture is sufficient and correct). This is a *narrower* filter than CER020's broad `.EndsWith(".resx")`: the project has exactly 2 resx files (`EmailsResource.{en,es}.resx`, verified), and 9.5i must emit constants only for the email resource, so the filter names the file precisely. If a future second `*.en.resx` is added, the generator must be revisited to scope per-resource (out of scope today — there's one resource).
2. Parses the `<data name="...">` keys (reusing CER020's `XDocument` + `Descendants("data")` approach; malformed XML → emits nothing, same defensive `try/catch`).
3. Splits each key at the first `.` into `{prefix}.{part}`, groups by prefix, and emits:

```csharp
// EmailKeys.g.cs — emitted; never hand-edited
namespace ProjectCeres.Common.Email
{
    internal static class EmailKeys
    {
        internal static class PasswordResetRequest
        {
            public const string Subject  = "PasswordResetRequest.Subject";
            public const string BodyText = "PasswordResetRequest.BodyText";
            public const string BodyHtml = "PasswordResetRequest.BodyHtml";
        }
        // ... 9 more nested classes, 30 constants total
    }
}
```

- **Namespace `ProjectCeres.Common.Email`** so `EmailComposer` (same namespace) references `EmailKeys.*` with no new `using`.
- The constant **value** keeps the original dotted string, so the runtime localizer lookup is byte-identical to today.
- **No identifier sanitization needed** — verified that all 10 prefixes and the 3 parts are already valid C# identifiers.
- **Emits no `DiagnosticDescriptor`** — pure `AddSource`. (See §4 for why this matters.)

**Generator robustness:**
- *Key with no `.`* → skipped (can't split; not a template key).
- *Key whose part ≠ Subject/BodyText/BodyHtml* (e.g. `Foo.Footer`) → emitted as a constant `Foo.Footer` — inert (Compose references only the three it needs).
- *Malformed XML* → `try/catch`, emits an empty (or header-only) `EmailKeys.g.cs`. The build still has CER020 + the C# compiler to surface real problems.

### 2.2 The consumer refactor — `EmailComposer.Compose`

The 3 interpolation lines (`EmailComposer.cs:28–30`) become a switch mapping each template to its three generated constants:

```csharp
var (subjectKey, bodyTextKey, bodyHtmlKey) = key switch
{
    EmailTemplateKey.PasswordResetRequest =>
        (EmailKeys.PasswordResetRequest.Subject,
         EmailKeys.PasswordResetRequest.BodyText,
         EmailKeys.PasswordResetRequest.BodyHtml),
    // ... one arm per template, 10 total
    _ => throw new System.ArgumentOutOfRangeException(nameof(key), key, null),
};

var subjectTpl  = _localizer[subjectKey].Value;
var bodyTextTpl = _localizer[bodyTextKey].Value;
var bodyHtmlTpl = _localizer[bodyHtmlKey].Value;
```

Everything below those lines — the `CurrentUICulture` try/finally, `HtmlEncoder`, CR/LF stripping, `string.Format` — is **unchanged**. The `IEmailComposer.Compose` signature is unchanged, so the 4 auth-service callers and `EmailComposerTests` don't change.

**Drift caught at compile time, both harmful directions:**
- *Resx key renamed/deleted* → generator stops emitting that constant → `Compose`'s reference fails to compile (`CS0117`). ✓
- *New `EmailTemplateKey` value, no switch arm* → C# non-exhaustive-switch warning + the `_ => throw` makes the gap explicit at runtime. ✓
- *Orphan resx prefix (no enum value)* → **deliberately not caught** (harmless unused strings; adding a diagnostic for it would mean a CER0xx descriptor + RS2008 row + soak — out of scope for a generator stage; see §6).

### 2.3 Keystone mechanic (verified against Roslyn docs)

Hand-written `Compose` referencing generator-emitted `EmailKeys` constants in the same compilation is the documented incremental-generator pattern (the Roslyn cookbook shows `UserClass` referencing a generator-emitted type). The generator reads the resx (`AdditionalText`) and emits constants — it does **not** analyze `Compose`'s syntax — so there is no generator↔consumer cycle. One-way: resx → constants → `Compose`.

---

## 3. Result — what's caught, and behavior preservation

| Change | Result |
|---|---|
| rename a resx key | `EmailKeys.X.Subject` constant disappears → `Compose` won't compile (`CS0117`) |
| add an `EmailTemplateKey` value, forget the resx + switch arm | non-exhaustive-switch warning at build; `_ => throw` at runtime |
| EN/ES key divergence | already CER020's job (unchanged) |
| nothing changes | identical emails — same keys, same localizer lookups, same output (pinned by the existing `EmailComposerTests`) |

**0 impact on `main`'s behavior** — the refactor is behavior-preserving; the generator only adds a new emitted file.

---

## 4. Lifecycle — pure generator, no soak, one sitting

**This is the key difference from CER005/CER006.** Those were *diagnostics* (`warning` → 48h C-2 soak → `error`). 9.5i emits **no `DiagnosticDescriptor`** — it only calls `AddSource`. Consequences (all verified against CER020's wiring):

- **No `AnalyzerReleases.Unshipped.md` row.** RS2008 governs `DiagnosticDescriptor` IDs only; a generator that adds no descriptor needs no release row. (CER020 *has* a row because it *does* report `CER020_ResxParityMissing`; 9.5i reports nothing.)
- **No `.editorconfig` severity line, no C-2 soak, no `warning`→`error` flip.**
- **9.5i ships complete and closes `[x]` in one session** — the roadmap's "ship generator + migrate all consumers in one commit" lands literally.

---

## 5. Files and tests

### 5.1 Files touched

| File | Change |
|---|---|
| `ProjectCeres.Analyzers/EmailKeysGenerator.cs` | new `IIncrementalGenerator` (~50 lines, CER020 pipeline) |
| `ProjectCeres/Common/Email/EmailComposer.cs` | rewrite the 3 key lines → enum switch over `EmailKeys.*` |
| `ProjectCeres.Analyzers.Tests/EmailKeysGeneratorTests.cs` | generator + compile-error tests |

No `Diagnostics.cs` change, no `AnalyzerReleases.Unshipped.md` change, no `.csproj` change (rides the existing analyzer wiring), no Annotations change, no resx change, no enum change. `EmailComposerTests.cs` **unchanged** (it's the behavior-preservation proof).

### 5.2 Test matrix

Generator tests use `CSharpSourceGeneratorTest<EmailKeysGenerator, DefaultVerifier>` (CER020's harness — NOT the analyzer harness). `TestState.AdditionalFiles.Add((name, resxContent))` feeds the resx; `TestState.GeneratedSources.Add((typeof(EmailKeysGenerator), "EmailKeys.g.cs", exactExpectedSource))` pins the emitted output; `ExpectedDiagnostics` asserts compiler errors (the `CS0117` idiom CER020's tests already establish).

| # | Test | Proves |
|---|---|---|
| 1 | feed a resx with `Foo.Subject`/`Foo.BodyText`/`Foo.BodyHtml` → assert emitted source contains `static class Foo` with the 3 `const string` members | the generator emits the right shape |
| 2 | feed a resx with only `Foo.Subject` → assert NO `Foo.BodyText` constant in the emitted source | the generator emits exactly what's in the resx (so a deletion really removes the constant) |
| 3 | feed malformed XML → assert it emits the header-only file and does not throw | the defensive parse holds |
| 4 | `TestCode` references `EmailKeys.Foo.Subject` but the fed resx lacks `Foo` → `ExpectedDiagnostics` asserts `CS0103`/`CS0117` (missing member) | **the compile-error guarantee — the roadmap's core requirement, pinned** |
| 5 | (no new test) the existing `EmailComposerTests` (18 cases, 10 templates × EN/ES) still pass against the refactored `Compose` | the refactor is behavior-preserving |

Test 5 is "the existing suite stays green" — run as part of the stage's verification, not a new test file.

### 5.3 Commit shape — one commit (forced)

One commit: generator + `Compose` refactor + generator tests. It **must** be one commit — `Compose` references `EmailKeys.*` constants that don't exist until the generator emits them, so the two cannot compile separately. This satisfies the roadmap's "one commit" requirement structurally. Then a docs commit (roadmap `[x]`, changelog, ADR-0077 note that the generator family gained its first real code-emitting member).

---

## 6. TDD ordering (different from the analyzers)

The analyzers used inverted-TDD (inert analyzer → red tests → gate). 9.5i differs because the generator and the refactor are mutually dependent:

1. **Write `EmailKeysGenerator` first** (it has no dependency — reads resx, emits constants).
2. **Prove it emits** (tests 1–3 — generator-shape tests, which pass once the generator works).
3. **Refactor `Compose`** against the now-emitted constants (it compiles only because step 1 emits them).
4. **Add the compile-error test** (test 4) and confirm `EmailComposerTests` still green (test 5).

The generator-shape tests (1–3) are written alongside the generator; test 4 (compile-error) is written after the refactor confirms the mechanic works end-to-end.

---

## 7. Deferred work

| Item | Destination | Tripwire |
|---|---|---|
| orphan-resx-prefix diagnostic (a resx prefix with no enum value) | not scheduled — harmless; would be its own tiny CER0xx if orphans ever occur | none (no orphans today; enum↔resx match perfectly) |

No other deferrals. **No flip deferral** — 9.5i has no soak (§4), so unlike 9.5f/9.5g the roadmap line closes `[x]` this session.

---

## 8. Open questions

None. All forks resolved in brainstorm:
1. What 9.5i is → typed-key-constant generator + `Compose` refactor (the string-key-migration premise was falsified).
2. Member shape → per-template nested constants (`EmailKeys.PasswordResetRequest.Subject`); `Compose` maps enum→trio via exhaustive switch (strongest compile-error guarantee).
3. Orphan check → skipped (keeps 9.5i a pure generator; no diagnostic, no soak, one sitting).
