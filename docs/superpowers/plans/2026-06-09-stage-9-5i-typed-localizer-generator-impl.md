# Typed Email-Key Source Generator (9.5i) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a source generator that turns each email-resource key into a named C# constant, and refactor `EmailComposer.Compose` to reference those constants — so renaming a resx key becomes a compile error instead of a silent runtime fallback.

**Architecture:** A new `EmailKeysGenerator : IIncrementalGenerator` in `ProjectCeres.Analyzers/` mirrors the existing `ResxParityGenerator` (CER020): it reads `EmailsResource.en.resx` via `AdditionalTextsProvider`, parses `<data name="...">` keys with `XDocument`, splits each at the dot into `{prefix}.{part}`, and emits `EmailKeys.g.cs` with a nested `static class` per prefix holding `const string` members per part. `EmailComposer.Compose` is rewritten from `_localizer[$"{key}.Subject"]` interpolation to an enum `switch` over `EmailKeys.*`. The generator emits **no diagnostic** — so there is no `AnalyzerReleases` row, no RS2008 concern, and no warning→error soak.

**Tech Stack:** C# / .NET 10, `Microsoft.CodeAnalysis.CSharp` 4.11.0 (netstandard2.0 analyzer/generator target), `System.Xml.Linq` (BCL), xUnit + `Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing` 1.1.4 (`CSharpSourceGeneratorTest<T, DefaultVerifier>` harness).

**Spec:** `docs/superpowers/specs/2026-06-09-stage-9-5i-typed-localizer-generator-design.md`

---

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `ProjectCeres.Analyzers/EmailKeysGenerator.cs` | the generator — read EmailsResource.en.resx → emit `EmailKeys.g.cs` | 1 |
| `ProjectCeres.Analyzers.Tests/EmailKeysGeneratorTests.cs` | generator-shape tests (1–3) + compile-error test (4) | 1, 2 |
| `ProjectCeres/Common/Email/EmailComposer.cs` | refactor `Compose` to switch over `EmailKeys.*` | 2 |

**Ordering note (NOT inverted-TDD — the two halves are mutually dependent):** The generator has no dependency, so it goes first and its shape is proven by tests against a small synthetic resx (Task 1). Only then can `Compose` be refactored to reference the emitted constants (Task 2) — `Compose` won't compile until the generator emits them. The compile-error guarantee test (Task 1 Step 7) and the behavior-preservation check (Task 2) follow. The whole feature lands in **one commit** (generator + refactor + tests) because `Compose` can't compile separately.

**Test-design note (small synthetic resx, like CER020's tests):** The generator-shape tests feed a *tiny* resx (1–2 fake templates), NOT the real 30-key file — so the `GeneratedSources.Add` expected-source string stays short and stable. The real 30-key resx is exercised by the existing `EmailComposerTests` compiling + passing against the refactored `Compose` (Task 2). This mirrors how `CER020_ResxParityGeneratorTests` uses a 2-key `Emails.en.resx` rather than the production file.

---

## Task 1: The generator + its shape/compile-error tests

**Files:**
- Create: `ProjectCeres.Analyzers/EmailKeysGenerator.cs`
- Create: `ProjectCeres.Analyzers.Tests/EmailKeysGeneratorTests.cs`

- [ ] **Step 1: Create the generator `EmailKeysGenerator.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace ProjectCeres.Analyzers;

[Generator(LanguageNames.CSharp)]
public sealed class EmailKeysGenerator : IIncrementalGenerator
{
    private const string EmailResxFileSuffix = "EmailsResource.en.resx";
    private const string GeneratedNamespace = "ProjectCeres.Common.Email";
    private static readonly string[] KnownParts = { "Subject", "BodyText", "BodyHtml" };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var emailResx = context.AdditionalTextsProvider
            .Where(f => f.Path.EndsWith(EmailResxFileSuffix, System.StringComparison.OrdinalIgnoreCase))
            .Collect();

        context.RegisterSourceOutput(emailResx, (spc, files) =>
        {
            // group keys by prefix: "PasswordResetRequest" -> { "Subject", "BodyText", "BodyHtml" }
            var byPrefix = new SortedDictionary<string, SortedSet<string>>(System.StringComparer.Ordinal);

            foreach (var file in files)
            {
                foreach (var key in ParseKeys(file))
                {
                    var dot = key.IndexOf('.');
                    if (dot <= 0 || dot >= key.Length - 1) continue; // no prefix.part split
                    var prefix = key.Substring(0, dot);
                    var part = key.Substring(dot + 1);
                    if (!IsValidIdentifier(prefix) || !IsValidIdentifier(part)) continue;
                    if (!byPrefix.TryGetValue(prefix, out var parts))
                    {
                        parts = new SortedSet<string>(System.StringComparer.Ordinal);
                        byPrefix[prefix] = parts;
                    }
                    parts.Add(part);
                }
            }

            spc.AddSource("EmailKeys.g.cs", Emit(byPrefix));
        });
    }

    private static string Emit(SortedDictionary<string, SortedSet<string>> byPrefix)
    {
        var sb = new StringBuilder();
        sb.Append("// <auto-generated>email-key constants emitted by ProjectCeres.Analyzers.EmailKeysGenerator</auto-generated>\n");
        sb.Append("namespace ").Append(GeneratedNamespace).Append("\n{\n");
        sb.Append("    internal static class EmailKeys\n    {\n");
        foreach (var prefix in byPrefix.Keys)
        {
            sb.Append("        internal static class ").Append(prefix).Append("\n        {\n");
            foreach (var part in byPrefix[prefix])
            {
                sb.Append("            public const string ").Append(part)
                  .Append(" = \"").Append(prefix).Append('.').Append(part).Append("\";\n");
            }
            sb.Append("        }\n");
        }
        sb.Append("    }\n}\n");
        return sb.ToString();
    }

    private static IEnumerable<string> ParseKeys(AdditionalText file)
    {
        var keys = new List<string>();
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
            // Malformed XML — emit nothing (CER020 + the compiler surface real problems).
        }
        return keys;
    }

    private static bool IsValidIdentifier(string s)
    {
        if (s.Length == 0) return false;
        if (!char.IsLetter(s[0]) && s[0] != '_') return false;
        for (int i = 1; i < s.Length; i++)
            if (!char.IsLetterOrDigit(s[i]) && s[i] != '_') return false;
        return true;
    }
}
```

Notes:
- `SortedDictionary` + `SortedSet` with `Ordinal` comparison make the emitted output **deterministic** (stable key order) — required so the test's pinned expected-source matches byte-for-byte.
- The filter `EndsWith("EmailsResource.en.resx")` scopes to the one email resource (project has exactly 2 resx files, verified). EN culture only — CER020 guarantees EN/ES parity.
- Emits `internal static class` (the keys are an internal implementation detail of `EmailComposer`; `internal` is sufficient and avoids widening the public API).

- [ ] **Step 2: Build the analyzer project to confirm the generator compiles**

Run: `dotnet build ProjectCeres.Analyzers/ProjectCeres.Analyzers.csproj`
Expected: **Build succeeded, 0 errors.** (No RS2008 — the generator defines no `DiagnosticDescriptor`. No new descriptor means no release-tracking row is needed.)

- [ ] **Step 3: Write the generator-shape tests (1–3) in `EmailKeysGeneratorTests.cs`**

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace ProjectCeres.Analyzers.Tests;

// Uses CSharpSourceGeneratorTest directly (CER020 pattern) because the generator
// operates on AdditionalTexts (.resx), not syntax nodes.
public class EmailKeysGeneratorTests
{
    private static CSharpSourceGeneratorTest<EmailKeysGenerator, DefaultVerifier> NewTest(
        string testCode = "// placeholder")
        => new()
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net100,
            TestCode = testCode,
        };

    private const string Header =
        "// <auto-generated>email-key constants emitted by ProjectCeres.Analyzers.EmailKeysGenerator</auto-generated>\n";

    // 1. A resx with one template's 3 parts -> emits the nested class with 3 const members.
    [Fact]
    public async Task Emits_Nested_Class_With_Const_Members()
    {
        const string resx = @"<?xml version=""1.0"" encoding=""utf-8""?>
<root>
  <data name=""Foo.Subject""><value>S</value></data>
  <data name=""Foo.BodyText""><value>T</value></data>
  <data name=""Foo.BodyHtml""><value>H</value></data>
</root>";

        const string expected = Header +
@"namespace ProjectCeres.Common.Email
{
    internal static class EmailKeys
    {
        internal static class Foo
        {
            public const string BodyHtml = ""Foo.BodyHtml"";
            public const string BodyText = ""Foo.BodyText"";
            public const string Subject = ""Foo.Subject"";
        }
    }
}
";
        var test = NewTest();
        test.TestState.AdditionalFiles.Add(("EmailsResource.en.resx", resx));
        test.TestState.GeneratedSources.Add((typeof(EmailKeysGenerator), "EmailKeys.g.cs", expected));
        await test.RunAsync();
    }

    // 2. A resx missing a part -> that const is NOT emitted (deletion really removes the member).
    [Fact]
    public async Task Omits_Const_When_Resx_Key_Absent()
    {
        const string resx = @"<?xml version=""1.0"" encoding=""utf-8""?>
<root>
  <data name=""Foo.Subject""><value>S</value></data>
</root>";

        const string expected = Header +
@"namespace ProjectCeres.Common.Email
{
    internal static class EmailKeys
    {
        internal static class Foo
        {
            public const string Subject = ""Foo.Subject"";
        }
    }
}
";
        var test = NewTest();
        test.TestState.AdditionalFiles.Add(("EmailsResource.en.resx", resx));
        test.TestState.GeneratedSources.Add((typeof(EmailKeysGenerator), "EmailKeys.g.cs", expected));
        await test.RunAsync();
    }

    // 3. Malformed XML -> emits the header-only shell, does not throw.
    [Fact]
    public async Task Emits_Empty_Shell_On_Malformed_Xml()
    {
        const string resx = @"<root><data name=""Foo.Subject""><value>oops"; // unterminated

        const string expected = Header +
@"namespace ProjectCeres.Common.Email
{
    internal static class EmailKeys
    {
    }
}
";
        var test = NewTest();
        test.TestState.AdditionalFiles.Add(("EmailsResource.en.resx", resx));
        test.TestState.GeneratedSources.Add((typeof(EmailKeysGenerator), "EmailKeys.g.cs", expected));
        await test.RunAsync();
    }
}
```

- [ ] **Step 4: Run the generator-shape tests**

Run: `dotnet test ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj --filter "FullyQualifiedName~EmailKeysGenerator"`
Expected: **3 passed.** If a test fails on an *expected-source mismatch*, the framework prints a diff between emitted and expected — adjust the `expected` string's whitespace/newlines to match the generator's actual output EXACTLY (do NOT change the generator to match a typo in the test; the generator's format in Step 1 is the source of truth — fix the test string). If the emit format genuinely needs to change, change BOTH the generator and all expected strings together.

- [ ] **Step 5: Add the compile-error guarantee test (test 4) to `EmailKeysGeneratorTests.cs`**

This is the roadmap's core requirement — prove that referencing a key the resx doesn't contain is a *compile* error. Add this method to the test class:

```csharp
    // 4. Hand-written code referencing a constant for a key NOT in the resx -> compile error.
    [Fact]
    public async Task Referencing_Absent_Key_Is_A_Compile_Error()
    {
        // resx defines Foo.* but the consumer references EmailKeys.Bar.Subject, which is never emitted.
        const string resx = @"<?xml version=""1.0"" encoding=""utf-8""?>
<root>
  <data name=""Foo.Subject""><value>S</value></data>
</root>";

        const string consumer = @"
namespace ProjectCeres.Common.Email
{
    internal static class Consumer
    {
        public static string Get() => {|#0:EmailKeys.Bar|}.Subject;
    }
}";
        const string expectedEmit = Header +
@"namespace ProjectCeres.Common.Email
{
    internal static class EmailKeys
    {
        internal static class Foo
        {
            public const string Subject = ""Foo.Subject"";
        }
    }
}
";
        var test = NewTest(consumer);
        test.TestState.AdditionalFiles.Add(("EmailsResource.en.resx", resx));
        test.TestState.GeneratedSources.Add((typeof(EmailKeysGenerator), "EmailKeys.g.cs", expectedEmit));
        // EmailKeys.Bar is a reference to a NESTED TYPE 'Bar' that the generator never emitted.
        // A missing nested type is CS0426: "The type name 'Bar' does not exist in the type 'EmailKeys'".
        // (If the harness instead reports CS0117 — missing MEMBER — use that; see Step 6: run and read.)
        test.ExpectedDiagnostics.Add(
            DiagnosticResult.CompilerError("CS0426").WithLocation(0).WithArguments("Bar", "ProjectCeres.Common.Email.EmailKeys"));
        await test.RunAsync();
    }
```

- [ ] **Step 6: Run test 4 and confirm the compile-error assertion passes**

Run: `dotnet test ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj --filter "FullyQualifiedName~EmailKeysGenerator"`
Expected: **4 passed.** If test 4 fails because the compiler error code or arguments differ (e.g. it reports `CS0426` or a different message-arg shape), read the actual diagnostic the framework reports and adjust `.WithArguments(...)` / the error code to match. The *intent* — "referencing a missing emitted member is a compiler error" — is what must hold; the exact `CSxxxx` code is whatever Roslyn emits for "type does not contain member 'Bar'". (Reference `EmailKeys.Bar` is a missing nested type → likely `CS0426` 'The type name Bar does not exist in the type EmailKeys'. If so, use `DiagnosticResult.CompilerError("CS0426").WithLocation(0).WithArguments("Bar", "ProjectCeres.Common.Email.EmailKeys")`. Pick whichever the harness actually reports — do not guess; run and read.)

- [ ] **Step 7: Commit the generator + its tests**

```bash
git add ProjectCeres.Analyzers/EmailKeysGenerator.cs ProjectCeres.Analyzers.Tests/EmailKeysGeneratorTests.cs
git commit -m "feat(9.5i): EmailKeysGenerator — emit nested const email keys from resx + generator/compile-error tests"
```

---

## Task 2: Refactor `EmailComposer.Compose` to use the generated constants

**Files:**
- Modify: `ProjectCeres/Common/Email/EmailComposer.cs` (the `Compose` method body, lines 28–30 region)

- [ ] **Step 1: Replace the 3 interpolation lines with the enum switch over `EmailKeys.*`**

In `EmailComposer.cs`, replace these three lines (currently lines 28–30):

```csharp
            var subjectTpl = _localizer[$"{key}.Subject"].Value;
            var bodyTextTpl = _localizer[$"{key}.BodyText"].Value;
            var bodyHtmlTpl = _localizer[$"{key}.BodyHtml"].Value;
```

with:

```csharp
            var (subjectKey, bodyTextKey, bodyHtmlKey) = key switch
            {
                EmailTemplateKey.PasswordResetRequest =>
                    (EmailKeys.PasswordResetRequest.Subject, EmailKeys.PasswordResetRequest.BodyText, EmailKeys.PasswordResetRequest.BodyHtml),
                EmailTemplateKey.PasswordChanged =>
                    (EmailKeys.PasswordChanged.Subject, EmailKeys.PasswordChanged.BodyText, EmailKeys.PasswordChanged.BodyHtml),
                EmailTemplateKey.PasswordResetCancelledEmailChange =>
                    (EmailKeys.PasswordResetCancelledEmailChange.Subject, EmailKeys.PasswordResetCancelledEmailChange.BodyText, EmailKeys.PasswordResetCancelledEmailChange.BodyHtml),
                EmailTemplateKey.EmailChangeVerifyNew =>
                    (EmailKeys.EmailChangeVerifyNew.Subject, EmailKeys.EmailChangeVerifyNew.BodyText, EmailKeys.EmailChangeVerifyNew.BodyHtml),
                EmailTemplateKey.EmailChangeRevokeOld =>
                    (EmailKeys.EmailChangeRevokeOld.Subject, EmailKeys.EmailChangeRevokeOld.BodyText, EmailKeys.EmailChangeRevokeOld.BodyHtml),
                EmailTemplateKey.EmailChangeConfirmed =>
                    (EmailKeys.EmailChangeConfirmed.Subject, EmailKeys.EmailChangeConfirmed.BodyText, EmailKeys.EmailChangeConfirmed.BodyHtml),
                EmailTemplateKey.EmailChangeConfirmedToOld =>
                    (EmailKeys.EmailChangeConfirmedToOld.Subject, EmailKeys.EmailChangeConfirmedToOld.BodyText, EmailKeys.EmailChangeConfirmedToOld.BodyHtml),
                EmailTemplateKey.EmailChangeRevokeNotificationToOld =>
                    (EmailKeys.EmailChangeRevokeNotificationToOld.Subject, EmailKeys.EmailChangeRevokeNotificationToOld.BodyText, EmailKeys.EmailChangeRevokeNotificationToOld.BodyHtml),
                EmailTemplateKey.LockoutUnlock =>
                    (EmailKeys.LockoutUnlock.Subject, EmailKeys.LockoutUnlock.BodyText, EmailKeys.LockoutUnlock.BodyHtml),
                EmailTemplateKey.RegistrationConfirmation =>
                    (EmailKeys.RegistrationConfirmation.Subject, EmailKeys.RegistrationConfirmation.BodyText, EmailKeys.RegistrationConfirmation.BodyHtml),
                _ => throw new System.ArgumentOutOfRangeException(nameof(key), key, null),
            };

            var subjectTpl = _localizer[subjectKey].Value;
            var bodyTextTpl = _localizer[bodyTextKey].Value;
            var bodyHtmlTpl = _localizer[bodyHtmlKey].Value;
```

Leave everything else in `Compose` (the `CurrentUICulture` try/finally, `HtmlEncoder`, CR/LF stripping, `string.Format`, the `EmailMessage` return) **unchanged**. No new `using` is needed — `EmailKeys` is in the same `ProjectCeres.Common.Email` namespace, and `ArgumentOutOfRangeException` is fully qualified.

- [ ] **Step 2: Build the main project — confirm the generator emits `EmailKeys` and `Compose` compiles against it**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: **Build succeeded, 0 errors.** This is the end-to-end proof: the generator (running as part of the build) emits `EmailKeys.g.cs` with all 10 nested classes from the real 30-key resx, and the refactored `Compose` references them successfully. If the build fails with `CS0117`/`CS0426` on an `EmailKeys.X.Y` reference, the generator did not emit that constant — meaning a real resx-key/enum mismatch (investigate the resx, do not work around it in `Compose`).

- [ ] **Step 3: Run the existing EmailComposer tests — confirm behavior is preserved (test 5)**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~EmailComposer"`
Expected: **all existing `EmailComposerTests` pass (18 cases, 10 templates × EN/ES).** These assert the actual rendered subject/body for every template in both cultures — if they pass unchanged, the refactor produced identical output (same keys, same localizer lookups). This is the behavior-preservation gate; do NOT modify these tests.

- [ ] **Step 4: Commit the refactor (amends into the one-commit shape)**

Because the spec calls for a single feature commit (generator + refactor together) and `Compose` only compiles once the generator exists, fold this into Task 1's commit:

```bash
git add ProjectCeres/Common/Email/EmailComposer.cs
git commit --amend --no-edit
```

(If you prefer two commits during development, that's fine locally — but the generator commit must come first, and the final history should present them together. Amend is the simplest way to honor the "one commit" requirement.)

---

## Task 3: Whole-solution verification

**Files:** none (verification)

- [ ] **Step 1: Build the whole solution clean**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: **0 errors.** The generated `EmailKeys.g.cs` is part of the compilation; the refactored `Compose` references it.

- [ ] **Step 2: Run the full analyzer test suite (generator tests + no regression)**

Run: `dotnet test ProjectCeres.Analyzers.Tests/ProjectCeres.Analyzers.Tests.csproj`
Expected: all pass — the 36 prior analyzer/generator tests + the 4 new `EmailKeysGenerator` tests (≈40 total). No existing test changed.

- [ ] **Step 3: Run the email integration tests (behavior preservation, full)**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~Email"`
Expected: all email tests pass — proves the refactor changed no observable behavior.

- [ ] **Step 4: No commit** — pure verification. If any step fails, stop and root-cause (do not weaken a test).

---

## Task 4: Docs — sync, changelog, roadmap tick (CLOSES [x] — no soak)

**Files:**
- Modify: `docs/roadmap-phase-three.md` (the 9.5i line — tick `[x]`, record the re-scope)
- Modify: `CHANGELOG.md`
- Modify: `docs/decisions/ADR-0077-roslyn-analyzers-for-invariant-enforcement.md` (note the generator family gained a real code-emitting member; the re-scope)
- Possibly: `docs/testing.md` (note the source-generator test added)

**Gate awareness:** 9.5i has NO soak (no diagnostic → no warning→error flip), so unlike 9.5f/9.5g this stage CLOSES `[x]` in this session. The pre-stage-close gate (Phase E) requires `sync-docs` + `changelog-sync` fired this session AND zero unchecked `[ ]` under the 9.5i line before the `[x]` edit — satisfy both.

- [ ] **Step 1: Run `sync-docs` against the diff.** Update ADR-0077 to note: 9.5i added `EmailKeysGenerator` (the generator family's first *typed-accessor* emitter, additive to CER020); and that the roadmap's string-key-migration premise was re-scoped to a typed-key generator because there were no string-key consumers (record the finding so future readers aren't confused).

- [ ] **Step 2: Rewrite the roadmap 9.5i line + tick `[x]`.** In `docs/roadmap-phase-three.md`, the 9.5i line's premise ("consumers migrate from string keys") is wrong. Replace the description with: "typed email-key source generator: `EmailKeysGenerator` emits nested `const string` keys per template from `EmailsResource.en.resx`; `EmailComposer.Compose` refactored to a switch over `EmailKeys.*` so a resx-key rename is a `CS0117` compile error (no string-key consumers existed to migrate — the one consumer was dynamic-keyed off `EmailTemplateKey`). CER020 stays as a sibling. Pure generator: no diagnostic, no soak — shipped + closed in one commit." Tick the line `[x]`. Add a close-out sub-bullet with the branch + commit + test counts.

- [ ] **Step 3: Run `changelog-sync`** and add under Added: "`EmailKeysGenerator` source generator — emits typed `EmailKeys.*` constants from the email resx so a resx-key rename becomes a compile error; `EmailComposer` refactored to use them. Additive to CER020's parity check."

- [ ] **Step 4: Confirm zero unchecked `[ ]` under the 9.5i heading, then commit**

```bash
git add docs/roadmap-phase-three.md CHANGELOG.md docs/decisions/ADR-0077-roslyn-analyzers-for-invariant-enforcement.md docs/testing.md
git commit -m "docs(9.5i): typed email-key generator shipped + closed [x] — re-scope recorded; sync ADR/changelog/testing"
```

---

## Notes for the executor

- **NOT inverted-TDD.** Generator first (Task 1, proven against a small synthetic resx), then the refactor (Task 2, which compiles only because the generator emits the constants). The two ship in one commit (Task 2 Step 4 amends).
- **Deterministic emit is load-bearing.** The `SortedDictionary`/`SortedSet` ordering in the generator makes the output stable so the test's pinned `GeneratedSources` string matches. If you change the emit format, change every expected-source string in the tests in the same edit.
- **The compile-error test's exact `CSxxxx` code: run and read, don't guess.** Referencing a missing nested type (`EmailKeys.Bar`) vs a missing member produces different codes (`CS0426` vs `CS0117`). Step 6 says to use whichever the harness actually reports.
- **Never weaken `EmailComposerTests`** — they are the behavior-preservation proof. If they fail after the refactor, the refactor changed behavior; fix `Compose`, not the tests.
- **No `AnalyzerReleases.Unshipped.md` row, no `.editorconfig` change** — 9.5i emits no diagnostic. Adding a release row for a descriptor that doesn't exist would itself be wrong.
- **Evidence bundle at close-out:** the Stop hook will want `build-matrix.json` + `turn-shape.json` for stage 9.5i (code changed). Generate via `tools/agent-env/build-matrix.sh 9.5i` + the turn-shape generator, as in 9.5f/9.5g. The reviewer-pipeline slot is N/A unless the diff touches `Common/Authentication/**` `Migrations/**` `Models/**` (it touches `Common/Email/` + `ProjectCeres.Analyzers/` — not watched paths).
