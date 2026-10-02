# Stage 13.a — Scrutor Assembly-Scanning DI Registration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace 79 of `ProjectCeres/Program.cs`'s 84 manual `AddScoped`/`AddSingleton` service registrations with Scrutor assembly scanning, so adding a new service in one of the scanned namespaces no longer requires a hand-written DI line.

**Architecture:** Add the `Scrutor` package, a `[RegisterAsSingleton]` escape-hatch attribute, and two `Scan(...)` rules in `Program.cs` — one `InNamespaces("ProjectCeres.Services")` (prefix-inclusive, covers `Services.Reports`), one `InExactNamespaces(...)` across four exact roots (`ProjectCeres.Admin`, `ProjectCeres.Common.Authentication`, `ProjectCeres.Common`, `ProjectCeres.Common.Email`) with a named two-class exclusion (`UserOwnershipInterceptor`, `RowLevelSecurityInterceptor`) from the interface-pair rule. Add a DI-completeness check (startup + test). Then remove the 79 now-redundant manual lines in incremental, test-gated batches matching `Program.cs`'s existing comment-block structure, leaving the 5 non-scannable-within-84 lines and the separate `AddDbContext`/`Configure`/`AddOptions`/`AddHttpClient` family untouched.

**Tech Stack:** .NET 10, ASP.NET Core DI, Scrutor 7.0.0+, xUnit + FluentAssertions, the project's `TestWebApplicationFactory`/bucketed integration-test harness.

**Spec:** `docs/superpowers/specs/2026-09-27-stage-13a-scrutor-di-scanning-design.md` (see §3b for the namespace-scope correction every task below assumes).

## Global Constraints

- **Scan namespace rules (spec §3b/§6), exact and non-negotiable:**
  - `InNamespaces("ProjectCeres.Services")` — prefix-inclusive, covers `ProjectCeres.Services.Reports`.
  - `InExactNamespaces("ProjectCeres.Admin", "ProjectCeres.Common.Authentication", "ProjectCeres.Common", "ProjectCeres.Common.Email")` — exact-match only. **Never** a bare `ProjectCeres.Common` prefix (`InNamespaces`) — it would wrongly sweep in `ProjectCeres.Common.Exceptions` (6 exception classes), `ProjectCeres.Common.Localization`, and `ProjectCeres.Common.RateLimiting`.
- **Interceptor exclusion (spec §3b), exact and non-negotiable:** `UserOwnershipInterceptor` and `RowLevelSecurityInterceptor` are excluded from the interface-pair scan rule (rule a) and registered only via the self-registered-concrete rule (rule b, `AsSelf()`), regardless of the EF-Core interfaces they implement. `Program.cs` resolves both exclusively by concrete type (`sp.GetRequiredService<UserOwnershipInterceptor>()`) — registering them by interface instead would break DI at the first removal batch.
- **Default lifetime for scanned registrations is `Scoped`** (spec D4). `[RegisterAsSingleton]` is the explicit escape hatch for classes that need `Singleton`.
- **The 5 non-scannable-within-84 lines and the separate `AddDbContext`/`Configure<T>`/`AddOptions<T>`/`AddHttpClient` family (spec §3, exact lists) are never touched by any task in this plan.**
- **Run the full test suite once per removal-batch task** (targeted enough to confirm the batch's own affected tests pass, but each batch task also runs the full suite once before its commit, per the spec's "localize any miswiring immediately" migration approach, §7 step 5) — not after every single line removal, and not skipped between batches either; this stage's blast radius (every integration test boots the DI graph) is exactly why batching-with-full-suite-checks exists.
- **No `Co-Authored-By` trailer in any commit** (CLAUDE.md).
- Per `docs/testing.md` § Rules: every test file edit in this plan is case "new test" (the DI-completeness check and the two negative tests) — this stage adds no new business logic, so no existing test's expected value should ever need to change. If any existing test's assertion needs to change to pass after a removal batch, stop and treat it as a miswiring to fix in the production registration, not a test to edit.

---

## Task 1: Add the Scrutor package + `[RegisterAsSingleton]` attribute

**Files:**
- Modify: `ProjectCeres/ProjectCeres.csproj`
- Create: `ProjectCeres/Common/RegisterAsSingletonAttribute.cs`

**Interfaces:**
- Produces: `[RegisterAsSingleton]` attribute class, usable on any class in a scanned namespace.

- [ ] **Step 1: Add the Scrutor package reference**

Run: `cd ProjectCeres && dotnet add package Scrutor --version 7.0.0`

- [ ] **Step 2: Run `dotnet restore` to confirm no version conflict**

Run: `dotnet restore ProjectCeres/ProjectCeres.csproj`
Expected: restores cleanly, no `NU1605`/version-downgrade errors (per spec §4, `Microsoft.Extensions.DependencyInjection.Abstractions >= 10.0.0` is already satisfied transitively by the ASP.NET Core 10 shared framework).

- [ ] **Step 3: Write the attribute**

```csharp
namespace ProjectCeres.Common;

/// <summary>
/// Escape hatch for the minority of Scrutor-scanned classes that need Singleton
/// lifetime instead of the default Scoped (Stage 13.a, spec D4). Apply directly
/// to the class being registered; the Scan(...) rules in Program.cs check for
/// this attribute and call WithSingletonLifetime() instead of WithScopedLifetime()
/// when present.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class RegisterAsSingletonAttribute : Attribute;
```

- [ ] **Step 4: Build to confirm it compiles**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/ProjectCeres.csproj ProjectCeres/Common/RegisterAsSingletonAttribute.cs
git commit -m "feat(13.a): add Scrutor package + RegisterAsSingleton attribute

No Scan(...) calls yet, no manual registrations removed. The attribute
is unused until Task 2 wires it into the scan rules."
```

---

## Task 2: Write the two `Scan(...)` calls + DI-completeness check

**Files:**
- Modify: `ProjectCeres/Program.cs` (add the `Scan(...)` calls; do NOT remove any manual registration lines yet — that starts at Task 3)
- Create: `ProjectCeres/Common/DiCompletenessCheck.cs`
- Create: `ProjectCeres.Tests/Integration/DiCompletenessTests.cs`

**Interfaces:**
- Consumes: `[RegisterAsSingletonAttribute]` (Task 1).
- Produces: `DiCompletenessCheck.VerifyAsync(IServiceProvider)` (or equivalent static method name — exact signature decided here, consumed by both the startup call and the test).

This task adds the scan infrastructure and the completeness check, but makes **no behavioral change yet** — every one of the 79 manual lines still exists alongside the new scan rules, so Scrutor's registrations and the manual ones coexist (the manual lines, registered later in file order, win on last-registration-wins semantics for single-interface resolutions; this is intentional and temporary — Task 3 onward removes the manual duplicates). This lets the scan infrastructure be verified in isolation before any removal risk.

- [ ] **Step 1: Add the two `Scan(...)` calls**

In `ProjectCeres/Program.cs`, immediately before the first manual service registration (the `builder.Services.AddSingleton<IUserScope, UserScope>();` line), add:

```csharp
// Stage 13.a — Scrutor assembly scanning. Two rules:
//   (a) any class implementing at least one interface (declared or inherited,
//       project-defined or framework) EXCEPT the two named EF-Core-interceptor
//       exclusions below -> AsImplementedInterfaces()
//   (b) any class implementing no interface, PLUS the two named exclusions
//       -> AsSelf()
// Default lifetime Scoped (spec D4); [RegisterAsSingleton] overrides to Singleton.
// See docs/superpowers/specs/2026-09-27-stage-13a-scrutor-di-scanning-design.md §3b
// for why the namespace list below is NOT a single InNamespaces("ProjectCeres.Common")
// prefix, and why UserOwnershipInterceptor/RowLevelSecurityInterceptor are excluded
// from rule (a) despite implementing EF-Core interfaces.
var interceptorExclusions = new HashSet<Type>
{
    typeof(ProjectCeres.Common.UserOwnershipInterceptor),
    typeof(ProjectCeres.Common.RowLevelSecurityInterceptor),
};

static ServiceLifetime LifetimeFor(Type type) =>
    type.IsDefined(typeof(ProjectCeres.Common.RegisterAsSingletonAttribute), inherit: false)
        ? ServiceLifetime.Singleton
        : ServiceLifetime.Scoped;

builder.Services.Scan(scan => scan
    .FromAssemblyOf<Program>()
    .AddClasses(classes => classes
        .InNamespaces("ProjectCeres.Services")
        .Where(t => !interceptorExclusions.Contains(t)))
    .UsingRegistrationStrategy(RegistrationStrategy.Skip)
    .AsImplementedInterfaces()
    .UsingLifetimeFactory(LifetimeFor)
    .AddClasses(classes => classes
        .InNamespaces("ProjectCeres.Services")
        .Where(t => t.GetInterfaces().Length == 0 || interceptorExclusions.Contains(t)))
    .UsingRegistrationStrategy(RegistrationStrategy.Skip)
    .AsSelf()
    .UsingLifetimeFactory(LifetimeFor));

builder.Services.Scan(scan => scan
    .FromAssemblyOf<Program>()
    .AddClasses(classes => classes
        .InExactNamespaces(
            "ProjectCeres.Admin",
            "ProjectCeres.Common.Authentication",
            "ProjectCeres.Common",
            "ProjectCeres.Common.Email")
        .Where(t => !interceptorExclusions.Contains(t)))
    .UsingRegistrationStrategy(RegistrationStrategy.Skip)
    .AsImplementedInterfaces()
    .UsingLifetimeFactory(LifetimeFor)
    .AddClasses(classes => classes
        .InExactNamespaces(
            "ProjectCeres.Admin",
            "ProjectCeres.Common.Authentication",
            "ProjectCeres.Common",
            "ProjectCeres.Common.Email")
        .Where(t => t.GetInterfaces().Length == 0 || interceptorExclusions.Contains(t)))
    .UsingRegistrationStrategy(RegistrationStrategy.Skip)
    .AsSelf()
    .UsingLifetimeFactory(LifetimeFor));
```

`RegistrationStrategy.Skip` is deliberate: while manual lines still exist (this task and Task 3's early batches), the scan must not throw on a duplicate registration or silently replace the manual one — `Skip` means "if this service type is already registered, don't register it again," so the manual lines (registered after the scan calls, in file order) are irrelevant to resolution order during the transition, and the scan becomes authoritative only once a manual line is actually deleted. Confirm `RegistrationStrategy` and `UsingLifetimeFactory` are the exact current Scrutor v7.0.0 API surface before writing this — if either name differs from what actually compiles, use the real name and note the deviation in your report (the spec's §4 API-surface confirmation covers `Scan`/`AddClasses`/`InNamespaces`/`AsImplementedInterfaces`/`AsSelf`/`WithScopedLifetime` directly; `RegistrationStrategy.Skip` and `UsingLifetimeFactory` were not independently re-verified against the package source in the spec and must be confirmed against the actual installed package's public API — e.g. via `dotnet build` error messages, IntelliSense-equivalent reflection, or the package's own XML doc comments — before this step is considered done).

- [ ] **Step 2: Build to confirm the scan calls compile against the real Scrutor API**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: 0 errors. If `RegistrationStrategy.Skip` or `UsingLifetimeFactory` don't exist on the installed Scrutor version, find the real equivalent (check the package's actual public API — `dotnet build` errors will name the closest overloads) and use that instead; document the substitution in this task's report.

- [ ] **Step 3: Write the DI-completeness check**

```csharp
using System.Reflection;

namespace ProjectCeres.Common;

/// <summary>
/// Stage 13.a (spec D5) — walks every constructor parameter of every concrete,
/// non-abstract, public class in the scanned namespaces and confirms the DI
/// container can resolve it. Converts a silent scan-miss (wrong namespace, an
/// interface nobody scanned) into one named, immediate failure instead of a
/// runtime NullReferenceException or InvalidOperationException far from the
/// actual mistake. Run both at real app startup (Program.cs) and as a fast
/// automated test (DiCompletenessTests.cs).
/// </summary>
public static class DiCompletenessCheck
{
    private static readonly string[] ScannedNamespacePrefixes =
    [
        "ProjectCeres.Services",
    ];

    private static readonly string[] ScannedExactNamespaces =
    [
        "ProjectCeres.Admin",
        "ProjectCeres.Common.Authentication",
        "ProjectCeres.Common",
        "ProjectCeres.Common.Email",
    ];

    /// <summary>
    /// Returns a list of (type, missing dependency type) pairs for every scanned
    /// class whose constructor dependency cannot be resolved from <paramref name="services"/>.
    /// Empty list means the DI graph is complete for every scanned class.
    /// </summary>
    public static List<(Type Type, Type MissingDependency)> FindUnresolvable(IServiceProvider services)
    {
        var failures = new List<(Type, Type)>();

        var scannedTypes = typeof(Program).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic)
            .Where(t => (t.Namespace != null && ScannedNamespacePrefixes.Any(p => t.Namespace == p || t.Namespace.StartsWith(p + ".", StringComparison.Ordinal)))
                     || (t.Namespace != null && ScannedExactNamespaces.Contains(t.Namespace)));

        using var scope = services.CreateScope();

        foreach (var type in scannedTypes)
        {
            var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            if (ctor is null) continue;

            foreach (var param in ctor.GetParameters())
            {
                try
                {
                    var resolved = scope.ServiceProvider.GetService(param.ParameterType);
                    if (resolved is null && !IsOptionalDependency(param))
                    {
                        failures.Add((type, param.ParameterType));
                    }
                }
                catch (Exception)
                {
                    failures.Add((type, param.ParameterType));
                }
            }
        }

        return failures;
    }

    private static bool IsOptionalDependency(ParameterInfo param) =>
        param.HasDefaultValue || Nullable.GetUnderlyingType(param.ParameterType) is not null;
}
```

- [ ] **Step 4: Wire the startup call**

In `ProjectCeres/Program.cs`, after `var app = builder.Build();` (find the exact line — do not guess; read the file to confirm where the built `app` first becomes available) and before `app.Run()`, add:

```csharp
// Stage 13.a (spec D5) — fail fast at boot if the scan missed anything, rather
// than a scattered runtime failure far from the actual miswiring.
if (!app.Environment.IsEnvironment("Testing"))
{
    var unresolvable = ProjectCeres.Common.DiCompletenessCheck.FindUnresolvable(app.Services);
    if (unresolvable.Count > 0)
    {
        var details = string.Join("\n", unresolvable.Select(f => $"  {f.Type.FullName} needs {f.MissingDependency.FullName}"));
        throw new InvalidOperationException($"DI completeness check failed. Unresolvable dependencies:\n{details}");
    }
}
```

The `IsEnvironment("Testing")` guard exists because the test-suite's `WebApplicationFactory`-based tests build the full host too — Step 5 below adds a dedicated, better-reported test for the same check, so this startup call would otherwise run twice (once per test factory boot) with a less useful failure message than the dedicated test gives. Confirm `"Testing"` is this project's actual test-environment name by checking how other `Program.cs` conditionals already branch on it (e.g. grep `IsEnvironment("Testing")` in the current file) before writing this — use whatever name the codebase already uses, not an assumed one.

- [ ] **Step 5: Write the completeness test**

```csharp
using FluentAssertions;
using ProjectCeres.Common;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// Stage 13.a (spec D5, §8) — the fast automated half of the DI-completeness
/// check. Boots the real DI graph via TestWebApplicationFactory and asserts
/// every scanned class's constructor dependencies resolve.
/// </summary>
[Collection("IntegrationParallel3")]
public class DiCompletenessTests : IntegrationTestBase<Bucket3Factory>
{
    public DiCompletenessTests(Bucket3Factory factory, Bucket3Database bucketDb) : base(factory, bucketDb) { }

    [Fact]
    public void Every_scanned_class_constructor_dependency_resolves()
    {
        var failures = DiCompletenessCheck.FindUnresolvable(Factory.Services);

        failures.Should().BeEmpty(
            because: "a Scrutor scan miss should fail here, with the exact type named, " +
                     "not as a scattered runtime failure in an unrelated test");
    }
}
```

Read `ProjectCeres.Tests/Integration/DbContextRegistrationTests.cs` first (or any other `IntegrationTestBase<Bucket3Factory>` consumer) to confirm the exact base-class constructor signature and `Factory`/`BucketDb` naming this project's convention uses — match it exactly rather than guessing from this brief alone.

- [ ] **Step 6: Run the new test**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~DiCompletenessTests"`
Expected: 1 passed, 0 failed. If it fails, the failure message names the exact type and missing dependency — this is expected to be informative if the scan rules in Step 1 have a real gap; investigate and fix the scan rule (not the test) before proceeding.

- [ ] **Step 7: Run the full existing suite once, to confirm the new scan rules coexisting with all 79 manual lines cause no regression**

Run: `dotnet build ProjectCeres.sln && dotnet test ProjectCeres.sln`
Expected: all green, same pass count as before this task (the scan is additive-but-inert right now, per `RegistrationStrategy.Skip` — it should change nothing observable while the manual lines still exist).

- [ ] **Step 8: Commit**

```bash
git add ProjectCeres/Program.cs ProjectCeres/Common/DiCompletenessCheck.cs ProjectCeres.Tests/Integration/DiCompletenessTests.cs
git commit -m "feat(13.a): add Scan(...) rules + DI-completeness check

Scan rules coexist with all 79 manual registrations via
RegistrationStrategy.Skip -- no behavioral change yet. Manual-line
removal starts at the next task, one Program.cs comment-block batch
at a time."
```

---

## Task 3: Remove manual lines — Stage 7 block (4 lines)

**Files:**
- Modify: `ProjectCeres/Program.cs`

**Interfaces:**
- Consumes: the Scan(...) rules from Task 2 (now becoming load-bearing for these 4 services for the first time).

This is the first real removal batch. Per spec §7 step 5, remove one batch, run the full suite, commit — before moving to the next batch. This batch is 4 lines, all in `ProjectCeres.Common`/root namespace, naming-symmetric pairs, no non-scannables mixed in.

- [ ] **Step 1: Remove the 4 lines**

In `ProjectCeres/Program.cs`, delete:

```csharp
builder.Services.AddSingleton<IUserScope, UserScope>();
builder.Services.AddScoped<IBackgroundJobScope, BackgroundJobScope>();
builder.Services.AddScoped<IUserJobRunner, UserJobRunner>();
```

and

```csharp
builder.Services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();
```

Leave `builder.Services.AddSingleton(TimeProvider.System);` and `builder.Services.AddHttpContextAccessor();` untouched (the first is the non-scannable instance registration; the second is a built-in ASP.NET Core extension method, not one of the 84). Leave `builder.Services.AddScoped<UserOwnershipInterceptor>();` and the `RowLevelSecurityInterceptor` line from this same block for a later batch (Task 4) — this batch is scoped to the 4 naming-symmetric pairs only, keeping each batch's blast radius small per spec §7.

- [ ] **Step 2: Run the full suite**

Run: `dotnet build ProjectCeres.sln && dotnet test ProjectCeres.sln`
Expected: all green, same pass count as Task 2's baseline. If anything fails, the DI-completeness test (Task 2) should be the first signal — read its failure message before investigating elsewhere.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Program.cs
git commit -m "refactor(13.a): remove 4 manual lines now covered by the scan

Batch 1/N: IUserScope, IBackgroundJobScope, IUserJobRunner,
ICurrentUserAccessor -- all naming-symmetric pairs in
ProjectCeres.Common/ProjectCeres.Common.Authentication."
```

---

## Task 4: Remove manual lines — the two interceptors (2 lines, the highest-risk batch)

**Files:**
- Modify: `ProjectCeres/Program.cs`

**Interfaces:**
- Consumes: the interceptor exclusion from Task 2's scan rules.

This is the batch the spec's §3b finding is specifically about. Isolated into its own task (not bundled with Task 3) because it is the one place a wrong scan rule would silently break DI in a way the other batches wouldn't.

- [ ] **Step 1: Remove the 2 lines**

In `ProjectCeres/Program.cs`, delete:

```csharp
builder.Services.AddScoped<UserOwnershipInterceptor>();
```

and

```csharp
builder.Services.AddScoped<RowLevelSecurityInterceptor>();
```

- [ ] **Step 2: Run `DbContextRegistrationTests` specifically first — this is the test that would catch a wrong scan treatment for these two classes**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~DbContextRegistrationTests"`
Expected: 5 passed, 0 failed — specifically `RowLevelSecurityInterceptor_is_registered_only_on_AppDbContext` and `UserOwnershipInterceptor_is_registered_on_both_DbContexts` must still pass; these directly exercise whether `sp.GetRequiredService<UserOwnershipInterceptor>()`/`sp.GetRequiredService<RowLevelSecurityInterceptor>()` (called inside the `AddDbContext` factory lambdas, lines ~89-90 and 99) still resolve now that the manual lines are gone.

- [ ] **Step 3: Run the full suite**

Run: `dotnet build ProjectCeres.sln && dotnet test ProjectCeres.sln`
Expected: all green, same pass count as Task 3's baseline.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Program.cs
git commit -m "refactor(13.a): remove the 2 interceptor manual lines

Batch 2/N: UserOwnershipInterceptor, RowLevelSecurityInterceptor --
scanned via AsSelf() per the Task 2 exclusion (spec 3b), since
Program.cs resolves both by concrete type, not by the EF-Core
interfaces they implement. DbContextRegistrationTests (5/5) confirms
both still resolve inside the AddDbContext factory lambdas."
```

---

## Task 5: Remove manual lines — Authentication block, part 1 (17 lines)

**Files:**
- Modify: `ProjectCeres/Program.cs`
- Modify: `ProjectCeres/Common/Authentication/TokenLookupHasher.cs`, `ProjectCeres/Common/Authentication/ErasurePseudonym.cs`, `ProjectCeres/Common/Authentication/LockoutCache.cs` (add `[RegisterAsSingleton]` to each)

- [ ] **Step 1: Remove the following lines**

```csharp
builder.Services.AddSingleton<TokenLookupHasher>();
builder.Services.AddSingleton<ErasurePseudonym>();
```

and, separately in the file (a short gap of unrelated code separates it from the block above — find and remove it in place, do not relocate it):

```csharp
builder.Services.AddSingleton<LockoutCache>();
```

and the rest of this batch:

```csharp
builder.Services.AddSingleton<ILookupNormalizer, LowercaseLookupNormalizer>();
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, Argon2idPasswordHasher>();
builder.Services.AddScoped<Argon2idPasswordHasher>();
builder.Services.AddScoped<PersistentTokenService>();
builder.Services.AddScoped<PasswordResetTokenGenerator>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<EmailConfirmationTokenGenerator>();
builder.Services.AddScoped<EmailConfirmationService>();
builder.Services.AddScoped<EmailChangeTokenGenerator>();
builder.Services.AddScoped<EmailChangeService>();
builder.Services.AddScoped<LockoutUnlockTokenGenerator>();
builder.Services.AddScoped<LockoutUnlockService>();
builder.Services.AddScoped<ExportTokenGenerator>();
builder.Services.AddScoped<ErasureTokenGenerator>();
```

That totals 17 lines removed from `Program.cs` in this task: 2 + 1 + 14.

`TokenLookupHasher`, `ErasurePseudonym`, and `LockoutCache` were all registered `AddSingleton` with no interface — each needs `[RegisterAsSingleton]` added to its class definition (they didn't need the attribute before this stage) so Task 2's `LifetimeFor` function registers them as `Singleton`, not the default `Scoped`. Add `[RegisterAsSingleton]` directly above each class declaration (e.g. `[RegisterAsSingleton]\npublic sealed class TokenLookupHasher ...`) in `ProjectCeres/Common/Authentication/TokenLookupHasher.cs`, `ProjectCeres/Common/Authentication/ErasurePseudonym.cs`, and `ProjectCeres/Common/Authentication/LockoutCache.cs`. Read each file first to confirm the exact current class declaration line before editing.

`IPasswordHasher<ApplicationUser>` is the spec's one closed-generic-BCL-interface case (§3) — confirm it is picked up by the scan (it should be, since `Argon2idPasswordHasher` lives in `ProjectCeres.Common.Authentication` and the interface-pair rule has no restriction against generic interfaces) by checking `Every_scanned_class_constructor_dependency_resolves` passes in Step 2 below; if `IPasswordHasher<ApplicationUser>` specifically fails to resolve, that is this task's one real risk and must be root-caused, not worked around.

- [ ] **Step 2: Run the full suite**

Run: `dotnet build ProjectCeres.sln && dotnet test ProjectCeres.sln`
Expected: all green, same pass count as Task 4's baseline.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Program.cs ProjectCeres/Common/Authentication/TokenLookupHasher.cs ProjectCeres/Common/Authentication/ErasurePseudonym.cs ProjectCeres/Common/Authentication/LockoutCache.cs
git commit -m "refactor(13.a): remove 17 manual lines, Authentication block part 1

Batch 3/N: password/token/lockout services. TokenLookupHasher,
ErasurePseudonym, LockoutCache gain [RegisterAsSingleton] to preserve
their Singleton lifetime under the scan's Scoped default."
```

---

## Task 6: Remove manual lines — Authentication block, part 2 + Admin (11 lines)

**Files:**
- Modify: `ProjectCeres/Program.cs`

- [ ] **Step 1: Remove the following lines**

```csharp
builder.Services.AddScoped<IEmailRecipientResolver, EmailRecipientResolver>();
builder.Services.AddScoped<IEmailComposer, EmailComposer>();
builder.Services.AddScoped<ILanguageResolver, LanguageResolver>();
builder.Services.AddSingleton<IResendSignatureVerifier, ResendSignatureVerifier>();
builder.Services.AddScoped<MfaBackupCodeService>();
builder.Services.AddScoped<TotpReplayGuard>();
builder.Services.AddScoped<FailedLoginRecorder>();
builder.Services.AddScoped<IAuditLogWriter, AuditLogWriter>();
builder.Services.AddSingleton<IAuthorizationHandler, RecentAuthRequirementHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, AdminLiveRequirementHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, RecentAuthMiddlewareResultHandler>();
```

`IResendSignatureVerifier`/`ResendSignatureVerifier`, the two `IAuthorizationHandler` registrations, and `IAuthorizationMiddlewareResultHandler` were all `AddSingleton` — each needs `[RegisterAsSingleton]` added to its class. `RecentAuthRequirementHandler` and `AdminLiveRequirementHandler` are the spec's base-class-inherited-interface case (`AuthorizationHandler<TRequirement>` implements `IAuthorizationHandler`) — **both must still resolve as `IEnumerable<IAuthorizationHandler>`, not just one of them** (spec §3's explicit note: ASP.NET Core runs every registered handler, so both registrations are by-design, never a last-registration-wins conflict). Confirm this specifically in Step 2.

Add `[RegisterAsSingleton]` to: `ProjectCeres/Common/Email/ResendSignatureVerifier.cs`, `ProjectCeres/Common/Authentication/RecentAuthRequirementHandler.cs`, `ProjectCeres/Admin/RequireAdminAttribute.cs` (where `AdminLiveRequirementHandler` is declared — confirm by reading the file first, since this class lives in a file named for something else per the spec's §3 finding), `ProjectCeres/Common/Authentication/RecentAuthMiddlewareResultHandler.cs`.

- [ ] **Step 2: Run the full suite, paying particular attention to any MFA/auth-handler test class**

Run: `dotnet build ProjectCeres.sln && dotnet test ProjectCeres.sln`
Expected: all green, same pass count as Task 5's baseline. If any test asserting both `RecentAuthRequirementHandler` and `AdminLiveRequirementHandler` run (or any test checking `IEnumerable<IAuthorizationHandler>` has 2+ entries) fails or drops to 1 handler, that is a scan-registration-strategy bug (likely `RegistrationStrategy.Skip` incorrectly deduplicating same-interface registrations) — do not work around it by special-casing; it must register both.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Program.cs ProjectCeres/Common/Email/ResendSignatureVerifier.cs ProjectCeres/Common/Authentication/RecentAuthRequirementHandler.cs ProjectCeres/Admin/RequireAdminAttribute.cs ProjectCeres/Common/Authentication/RecentAuthMiddlewareResultHandler.cs
git commit -m "refactor(13.a): remove 11 manual lines, auth-handler block

Batch 4/N: email/MFA support services + the 3 auth-handler
registrations, including the dual IAuthorizationHandler registration
(RecentAuthRequirementHandler, AdminLiveRequirementHandler) -- both
must resolve, confirmed via full-suite run, not deduplicated to one."
```

---

## Task 7: Remove manual lines — Services block, part 1 (18 lines)

**Files:**
- Modify: `ProjectCeres/Program.cs`

- [ ] **Step 1: Remove the following lines**

```csharp
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<CategorySeedService>();
builder.Services.AddScoped<ILiabilityPaymentService, LiabilityPaymentService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<ITransactionExportService, TransactionExportService>();
builder.Services.AddScoped<ITransferService, TransferService>();
builder.Services.AddScoped<IExportJobService, ExportJobService>();
builder.Services.AddScoped<IErasureService, ErasureService>();
builder.Services.AddScoped<DataExportBuilder>();
builder.Services.AddScoped<ErasureExecutor>();
builder.Services.AddScoped<IRecurringTransactionService, RecurringTransactionService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<NetWorthGenerator>();
builder.Services.AddScoped<IncomeExpenseGenerator>();
builder.Services.AddScoped<ExpenseBreakdownGenerator>();
builder.Services.AddScoped<TransactionHistoryGenerator>();
```

- [ ] **Step 2: Run the full suite**

Run: `dotnet build ProjectCeres.sln && dotnet test ProjectCeres.sln`
Expected: all green, same pass count as Task 6's baseline.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Program.cs
git commit -m "refactor(13.a): remove 18 manual lines, Services block part 1

Batch 5/N: account/transaction/export/erasure services + the first
4 report generators (ProjectCeres.Services.Reports, covered by the
InNamespaces prefix-inclusive rule, not a separate namespace entry)."
```

---

## Task 8: Remove manual lines — Services block, part 2 (16 lines)

**Files:**
- Modify: `ProjectCeres/Program.cs`

**Note (added after Task 7's execution):** Task 7 originally planned 18 lines but narrowed to 14 after its implementer found a real scan-mechanism gap pre-emptively: all 8 `IReportGenerator` implementations are injected by concrete type in `ReportGeneratorFactory`'s constructor, not by the interface, so `AsImplementedInterfaces()` alone would leave the concrete types unresolvable. The fix (extending `selfWithInterfacesTargets` to all 8 generator classes, landed in Task 7's commit) is already in place and covers both this task's 4 generators AND the 4 Task 7 held back (`NetWorthGenerator`, `IncomeExpenseGenerator`, `ExpenseBreakdownGenerator`, `TransactionHistoryGenerator`). Both sets of 4 are equally safe to remove now — this task's line list below has grown from 12 to 16 to absorb Task 7's 4 held-back lines, since they're the same mechanism, same file region, and would otherwise have no receiving task.

- [ ] **Step 1: Remove the following lines**

```csharp
builder.Services.AddScoped<NetWorthGenerator>();
builder.Services.AddScoped<IncomeExpenseGenerator>();
builder.Services.AddScoped<ExpenseBreakdownGenerator>();
builder.Services.AddScoped<TransactionHistoryGenerator>();
builder.Services.AddScoped<BudgetVsActualReportGenerator>();
builder.Services.AddScoped<LargestExpensesReportGenerator>();
builder.Services.AddScoped<MonthlyCashFlowReportGenerator>();
builder.Services.AddScoped<NetWorthOverTimeReportGenerator>();
builder.Services.AddScoped<ReportGeneratorFactory>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IFileAttachmentService, FileAttachmentService>();
builder.Services.AddScoped<ISupportTicketService, SupportTicketService>();
builder.Services.AddScoped<ISupportMessageService, SupportMessageService>();
builder.Services.AddScoped<ISupportRecipientResolver, SupportRecipientResolver>();
builder.Services.AddScoped<ISupportNotificationService, SupportNotificationService>();
builder.Services.AddScoped<INewSessionNotificationService, NewSessionNotificationService>();
```

Note: `NetWorthGenerator`/`IncomeExpenseGenerator`/`ExpenseBreakdownGenerator`/`TransactionHistoryGenerator` are no longer present in `Program.cs` as of Task 9's start if Task 7 already removed them in a prior pass — check the file first; if they're already gone, remove only the remaining 12 named above. `ReportGeneratorFactory` itself has no interface (self-registered-concrete, handled by the plain `AsSelf()` rule, not `selfWithInterfacesTargets` — it's the consumer of the 8 generators, not one of them).

Leave `builder.Services.Configure<FileAttachmentOptions>(...)` (the line immediately preceding `IFileAttachmentService` in the current file) untouched — it is one of the separate, never-part-of-84 `Configure<T>` family.

- [ ] **Step 2: Run the full suite**

Run: `dotnet build ProjectCeres.sln && dotnet test ProjectCeres.sln`
Expected: all green, same pass count as Task 7's baseline.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Program.cs
git commit -m "refactor(13.a): remove 16 manual lines, Services block part 2

Batch 6/N: all 8 IReportGenerator implementations (4 held back from
Task 7 pending the selfWithInterfacesTargets fix, now safe + the 4
originally planned here), dashboard, file attachments, support-ticket
services (3 of which -- SupportRecipientResolver,
SupportNotificationService, NewSessionNotificationService -- live in
ProjectCeres.Common.Email, scanned via the InExactNamespaces rule)."
```

---

## Task 9: Remove manual lines — Services block, part 3 + AdminRoleService (15 lines)

**Files:**
- Modify: `ProjectCeres/Program.cs`

- [ ] **Step 1: Remove the following lines**

```csharp
builder.Services.AddScoped<IBudgetService, BudgetService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<ICategoryBudgetService, CategoryBudgetService>();
builder.Services.AddScoped<IMovementService, MovementService>();
builder.Services.AddScoped<IMovementExportService, MovementExportService>();
builder.Services.AddScoped<IImportProfileService, ImportProfileService>();
builder.Services.AddSingleton<CsvImportParser>();
builder.Services.AddSingleton<ExcelImportParser>();
builder.Services.AddSingleton<ImportParserFactory>();
builder.Services.AddScoped<IImportService, ImportService>();
builder.Services.AddScoped<ITransferDetectionService, TransferDetectionService>();
builder.Services.AddScoped<ITransferReviewService, TransferReviewService>();
builder.Services.AddScoped<IImportStagedTransactionService, ImportStagedTransactionService>();
builder.Services.AddScoped<IHeaderDetectionService, HeaderDetectionService>();
builder.Services.AddScoped<AdminRoleService>();
```

`CsvImportParser`, `ExcelImportParser`, `ImportParserFactory` were `AddSingleton` with no interface — add `[RegisterAsSingleton]` to each class (`ProjectCeres/Services/CsvImportParser.cs`, `ProjectCeres/Services/ExcelImportParser.cs`, `ProjectCeres/Services/ImportParserFactory.cs`). `AdminRoleService` lives in `ProjectCeres.Admin` — this is the last line in this batch and the last of the 79 total.

- [ ] **Step 2: Run the full suite**

Run: `dotnet build ProjectCeres.sln && dotnet test ProjectCeres.sln`
Expected: all green, same pass count as Task 8's baseline.

- [ ] **Step 3: Verify all 79 manual lines are gone**

Run: `grep -cE "builder\.Services\.Add(Scoped|Singleton|Transient)" ProjectCeres/Program.cs`
Expected: exactly 5 — the non-scannable-within-84 lines from spec §3 (`TimeProvider.System`, the 3 `IEmailService` conditional branches, `IBreachedPasswordChecker`'s E2E branch). If the count is not 5, a line was missed in an earlier batch — find it and fold its removal into this task before committing.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Program.cs ProjectCeres/Services/CsvImportParser.cs ProjectCeres/Services/ExcelImportParser.cs ProjectCeres/Services/ImportParserFactory.cs
git commit -m "refactor(13.a): remove final 15 manual lines -- all 79 scanned registrations now scan-only

Batch 7/7: budget/session/movement/import services + AdminRoleService.
grep confirms exactly 5 Add{Scoped,Singleton,Transient} calls remain
in Program.cs -- the non-scannable-within-84 set from spec §3."
```

---

## Task 10: Negative tests + docs sync + roadmap close-out

**Files:**
- Create: `ProjectCeres.Tests/Unit/ScrutorNamespaceBoundaryTests.cs`
- Modify: `docs/architecture.md`
- Modify: wherever the `IUserOwned` 5-registry checklist is documented (search for the text first — do not assume a file path)
- Modify: `docs/roadmap-phase-three.md`

- [ ] **Step 1: Write the two negative tests from spec §8**

```csharp
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common;

namespace ProjectCeres.Tests.Unit;

/// <summary>
/// Stage 13.a (spec §8) -- proves the scan's namespace boundaries are real,
/// not accidentally matching everything in the assembly.
/// </summary>
public class ScrutorNamespaceBoundaryTests
{
    // A throwaway class OUTSIDE every scanned namespace, with an interface --
    // must never be picked up regardless of its naming or interface shape.
    private interface IOutsideScanScope { }
    private sealed class OutsideScanScope : IOutsideScanScope { }

    [Fact]
    public void Class_outside_scanned_namespaces_is_not_registered()
    {
        var services = new ServiceCollection();
        services.Scan(scan => scan
            .FromAssemblyOf<Program>()
            .AddClasses(classes => classes.InNamespaces("ProjectCeres.Services"))
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.Any(d => d.ServiceType == typeof(IOutsideScanScope)).Should().BeFalse(
            "this test's own interface/class pair is declared in ProjectCeres.Tests.Unit, " +
            "never ProjectCeres.Services -- it must not be swept in");
    }

    [Fact]
    public void Class_in_CommonExceptions_sibling_namespace_is_not_registered_by_the_Common_scan()
    {
        // Mirrors the real risk spec §3b found: a bare InNamespaces("ProjectCeres.Common")
        // would sweep in ProjectCeres.Common.Exceptions. This proves InExactNamespaces
        // does NOT make that mistake, using this test file's own throwaway type instead
        // of a real exception class (never commit a throwaway type into ProjectCeres/).
        var services = new ServiceCollection();
        services.Scan(scan => scan
            .FromAssemblyOf<Program>()
            .AddClasses(classes => classes.InExactNamespaces("ProjectCeres.Common"))
            .AsImplementedInterfaces()
            .WithScopedLifetime());

        services.Any(d => d.ServiceType == typeof(IOutsideScanScope)).Should().BeFalse(
            "InExactNamespaces(\"ProjectCeres.Common\") must not match a class in a " +
            "different namespace even if that namespace starts with \"ProjectCeres.Common.\"");
    }

    [Fact]
    public void RegisterAsSingleton_attribute_changes_lifetime_from_the_Scoped_default()
    {
        // Spec §8's last bullet: confirm the attribute actually changes lifetime for a
        // real case. TokenLookupHasher carries [RegisterAsSingleton] as of Task 5.
        //
        // CORRECTION (discovered during Task 2, confirmed via reflection against the
        // real installed Scrutor 7.0.0 DLL -- not assumed): UsingLifetimeFactory does
        // not exist anywhere on Scrutor 7.0.0's ILifetimeSelector. The only members are
        // the three fixed-lifetime terminals WithSingletonLifetime()/WithScopedLifetime()/
        // WithTransientLifetime() -- no overload takes a Func<Type, ServiceLifetime>.
        // Program.cs's real Scan(...) calls (since Task 2) use two separate AddClasses
        // batches instead -- one filtered to [RegisterAsSingleton] classes terminating in
        // WithSingletonLifetime(), one filtered to everything else terminating in
        // WithScopedLifetime(). This test reproduces that exact real shape instead of the
        // nonexistent single-call API the original brief draft assumed.
        var services = new ServiceCollection();
        services.Scan(scan => scan
            .FromAssemblyOf<Program>()
            .AddClasses(classes => classes
                .InExactNamespaces("ProjectCeres.Common.Authentication")
                .Where(t => t.IsDefined(typeof(RegisterAsSingletonAttribute), inherit: false)))
            .UsingRegistrationStrategy(RegistrationStrategy.Skip)
            .AsSelf()
            .WithSingletonLifetime()
            .AddClasses(classes => classes
                .InExactNamespaces("ProjectCeres.Common.Authentication")
                .Where(t => !t.IsDefined(typeof(RegisterAsSingletonAttribute), inherit: false)))
            .UsingRegistrationStrategy(RegistrationStrategy.Skip)
            .AsSelf()
            .WithScopedLifetime());

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ProjectCeres.Common.Authentication.TokenLookupHasher));
        descriptor.Should().NotBeNull("TokenLookupHasher must be scanned as a self-registered concrete");
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton,
            "TokenLookupHasher carries [RegisterAsSingleton], which must override the Scoped default");
    }
}
```

- [ ] **Step 2: Run all three new tests**

Run: `dotnet test ProjectCeres.Tests/ProjectCeres.Tests.csproj --filter "FullyQualifiedName~ScrutorNamespaceBoundaryTests"`
Expected: 3 passed, 0 failed.

- [ ] **Step 3: Update `docs/architecture.md`**

Read the file first to find where the Services-layer registration convention is currently (or isn't) documented — the spec confirms this is "currently silent, a doctrinal gap, not a contradiction to resolve." Add a new subsection documenting: the scan rule (structural interface-implementation or self-registered-concrete, never a naming pattern — spec D1a), the exact namespace list (`ProjectCeres.Services` prefix-inclusive; `ProjectCeres.Admin`/`ProjectCeres.Common.Authentication`/`ProjectCeres.Common`/`ProjectCeres.Common.Email` exact-match), the `[RegisterAsSingleton]` escape hatch, the two-class interceptor exclusion and why, and the 5-line non-scannable boundary (instance registration + 4 conditional branches) that stays manual.

- [ ] **Step 4: Update the `IUserOwned` 5-registry documentation**

Search for the existing checklist text (grep for "5-registry" or "IUserOwned" across `docs/` and `.claude/` memory files per the spec's own pointer to `feedback_iuserowned_requires_five_registries`). Reword the DI-registration step from "add a line to `Program.cs`" to "name the class/interface per the scan convention (per `docs/architecture.md`'s new subsection); the scan + DI-completeness check cover it automatically."

- [ ] **Step 5: Close out the roadmap**

In `docs/roadmap-phase-three.md`'s `## Stage 13.a` section, tick every checklist item, matching the resolution-note style used elsewhere in this file (name the actual commits, the actual final state). Change the status line from `❌ Pending` to `✅ Done (<today's date>)`, matching the heading convention confirmed in Stage 16.5's own close-out (`## Stage N — Title ✅ Done (date)`, not a parenthetical).

- [ ] **Step 6: Run the full suite one final time**

Run: `dotnet build ProjectCeres.sln && dotnet test ProjectCeres.sln`
Expected: all green.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres.Tests/Unit/ScrutorNamespaceBoundaryTests.cs docs/architecture.md docs/roadmap-phase-three.md
git commit -m "docs(13.a): negative tests + architecture docs + roadmap close-out

Stage 13.a complete: 79 of 84 Program.cs service registrations now
scan-driven; the remaining 5 (an instance registration + 4
environment-conditional branches) and the separate DbContext/
Configure/AddHttpClient family stay manual by design. Adds the two
namespace-boundary negative tests plus a direct confirmation that
[RegisterAsSingleton] overrides the scan's Scoped default (spec §8)."
```

---

## Verification

After Task 10 completes, confirm:

- `grep -cE "builder\.Services\.Add(Scoped|Singleton|Transient)" ProjectCeres/Program.cs` returns exactly 5.
- `dotnet build ProjectCeres.sln` — 0 errors, 0 new warnings.
- `dotnet test ProjectCeres.sln` — all green, same total pass count as the pre-Task-1 baseline plus exactly 4 new tests (`DiCompletenessTests` ×1, `ScrutorNamespaceBoundaryTests` ×3) — this stage changes no existing test's behavior.
- `docs/roadmap-phase-three.md`'s Stage 13.a section has no remaining `[ ]` items.
- Every class carrying `[RegisterAsSingleton]` was previously `AddSingleton` with no interface, or `AddSingleton<Interface, Class>` — confirm no class was accidentally given `Singleton` lifetime that was previously `Scoped`, or vice versa, by cross-checking the attribute list against spec §3's original `AddSingleton` call sites.
