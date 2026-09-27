# Stage 13.a — Scrutor Assembly-Scanning DI Registration (Design Spec)

> **Diataxis type:** Reference — design for a new standalone top-level stage, consumed by `superpowers:writing-plans` next.
>
> **Stage id:** `13.a` — a top-level `## Stage 13.a` heading in `docs/roadmap-phase-three.md`, a sibling of `## Stage 13` and `## Stage 14` (same shape as `## Stage 12.13`–`12.19`), NOT a row inside Stage 13's own `### Sub-stages` table. Deliberately letter-suffixed, not `13.12`: Stage 13's numeric sub-stages (13.1–13.11) are all GDPR-baseline items tracked against `legal.md`/`security-model.md` and nested under Stage 13's umbrella; this stage is an unrelated infrastructure change (how services get wired into DI) that the user asked to log inside the Stage 13 family — meaning positioned alongside it in the roadmap file, not folded into its GDPR sub-stage table — rather than the Stage 12 infra family, without colliding with an existing GDPR sub-stage number. `13.1` is taken ("Privacy policy text + page") — confirmed by reading `docs/roadmap-phase-three.md` line 1818 before choosing this id. (First attempt at this wrongly added it as a row in Stage 13's sub-stage table, which mislabels it as GDPR-related; corrected to a standalone top-level heading between Stage 13 and Stage 14.)

---

## 1. Purpose

Replace `ProjectCeres/Program.cs`'s manually-written interface-to-implementation service registrations with [Scrutor](https://github.com/khellang/Scrutor) assembly scanning, so adding a new service no longer requires a hand-written `builder.Services.AddScoped<IFoo, Foo>()` line to be remembered. This also removes the DI-registration step from the project's `IUserOwned` "5-registry" checklist for new user-owned entities, per the user's explicit choice (§2 D1).

## 2. Decisions locked in brainstorming (2026-09-27)

| # | Decision | Rationale |
|---|---|---|
| D1 | **Full scan, no exceptions**, for everything that has an interface-to-class OR a bare-concrete-class shape. | User's explicit choice over a partial-adoption option that would have kept the `IUserOwned` checklist's DI line manual. Confirmed twice — once before the concrete-class-count correction (§3), once after. |
| D1a | **Naming symmetry between interface and class is NOT a scan requirement.** The interface-pair scan rule is structural ("implements at least one interface, declared or inherited"), not name-pattern-based ("`FooService` implements `IFooService`"). This includes the 5 naming-mismatched-and-non-conditional pairs (2 of which are also base-class-inherited-interface cases) and the 1 closed-generic-BCL-interface pair (§3) in the scan, consistent with D1. | Added on second review: an earlier draft of §6 described the scan as matching "a naming pattern," which would have silently excluded 6 legitimate services from the scan by its own stated rule. Scrutor's actual mechanism (`Type.GetInterfaces()` via reflection) never required name matching — only this spec's prose did. |
| D2 | **Self-registered concrete classes (no interface) are IN scope** — scanned via a namespace rule, not left manual. | The initial brainstorm draft assumed these ~24 classes (`ErasureExecutor`, `PasswordResetService`, `AdminRoleService`, the report generators, etc.) belonged in the same manual bucket as true non-scannables. Counting the actual file (§3) showed they are a distinct, scannable category with no interface to match against — user chose to include them once the distinction was surfaced, keeping "full scan, no exceptions" meaningful. (Second review corrected the count to 36; the decision itself is unchanged.) |
| D3 | **True non-scannables stay manual, named explicitly as out-of-policy-scope, not as an exception to D1.** | These have no interface-to-class shape at all (a `DbContext`, an `Options` binder, a typed `HttpClient`, an environment-conditional branch) — Scrutor's convention scan has nothing to match against. The spec states this so it reads as "never in scope" rather than "scope creep back toward partial adoption." |
| D4 | **Default lifetime for scanned registrations is `Scoped`**, with a `[RegisterAsSingleton]` marker attribute as the documented escape hatch for the minority that need `Singleton`. | Matches the overwhelming majority of today's registrations (verified in §3). A per-class attribute is simpler than a second naming convention and is greppable. |
| D5 | **A startup-time DI-completeness check is mandatory**, run both at real app boot and as a fast automated test. | Full-scan adoption converts a compile-time-adjacent mistake (forgetting a line) into a silent scan-miss (wrong name, wrong namespace, missing the naming pattern) that would otherwise only surface as a runtime `null`/resolution exception far from the actual mistake. User chose this over "rely on the existing test suite to catch it eventually." |

No ADR is required for the Scrutor package addition itself — this project has no NuGet-side pinning-ADR convention (confirmed absent, `docs/decisions/` searched; only the pnpm-side ADR-0079 exists) and Scrutor introduces no transitive-advisory conflict to document. If a future NuGet override is ever needed for Scrutor's own dependency tree, that is the trigger for originating the first NuGet-side ADR — not this stage.

## 3. Deviations from the initial brainstorm draft (two independent verification passes — READ)

The brainstorm's first pass, based on `ceres-researcher`'s dispatch, characterized `Program.cs`'s registrations as roughly "~50 interface pairs plus a smaller set of concrete-only/options/factory registrations." A first `verify-against-codebase` pass corrected this to a three-way split (49 interface-pairs / ~24 concretes / ~12 non-scannables). **A second, independent review (Opus, plan-mode) parsed every one of the 84 `builder.Services.Add{Scoped,Singleton,Transient}(...)` call sites in `Program.cs` programmatically (a bracket-aware regex over the raw source, re-run twice, each result cross-checked against a manual read of the matched lines)** — and found the "corrected" numbers were themselves wrong, that two revision attempts within this same review session also produced internally-inconsistent arithmetic before landing on the version below, and that the root cause of every miscount was conflating `Add{Scoped,Singleton,Transient}` (84 calls) with `AddDbContext`/`Configure<T>`/`AddOptions<T>` (a **separate, non-overlapping set of extension methods that were never part of the 84 to begin with**). This is now the authoritative, arithmetically-closed count (79 + 5 = 84, exactly, no residual); do not re-derive it from an earlier, superseded version of this section.

| Category | Count | Scan treatment |
|---|---|---|
| Interface-to-class pairs, naming-symmetric (`IFoo` implemented by `Foo`) | **37** | Scanned — `AsImplementedInterfaces()` |
| Interface-to-class pairs, **naming-mismatched, non-conditional** (class name does not echo the interface name, AND not inside an `if (builder.Environment...)` branch) | **5** (exact list below) | **Scanned** — Scrutor's `AsImplementedInterfaces()` matches by actual implemented-interface list via reflection, not by name; naming symmetry was never a Scrutor requirement, only an assumption in an earlier draft of this spec's own wording (§6 fixes this) |
| **Closed generic BCL interface** (`IPasswordHasher<ApplicationUser>` implemented by `Argon2idPasswordHasher`) | **1** | **Scanned** (§2 D1a) — structurally identical to any other interface-pair once you stop requiring the interface to be project-defined |
| Self-registered concrete classes (no interface; registered by own type) | **36** | **Scanned** (D2) — `AsSelf()` |
| Non-scannable **within the 84** (an instance registration, plus every `Add{Scoped,Singleton,Transient}` call inside an environment-conditional branch, naming-symmetric or not) | **5** (exact list below) | Stays manual (D3) |

**37 + 5 + 1 + 36 = 79 scanned** (D1/D1a/D2's real scope) **+ 5 non-scannable = 84.** Exact, no rounding, no residual gap.

**Exact scanned-but-naming-mismatched list, all 5** (none of these are inside a conditional branch):
- `ICurrentUserAccessor` → `HttpContextCurrentUserAccessor` (line 73)
- `ILookupNormalizer` → `LowercaseLookupNormalizer` (line 146) — also a framework interface (`Microsoft.AspNetCore.Identity.ILookupNormalizer`), noted for completeness; scanned the same as any other interface-pair
- `IAuthorizationHandler` → `RecentAuthRequirementHandler` (line 239) and → `AdminLiveRequirementHandler` (line 240) — both also **base-class-inherited-interface** cases: `AuthorizationHandler<TRequirement>` implements `IAuthorizationHandler`, and neither class declares `: IAuthorizationHandler` directly. `Type.GetInterfaces()` includes inherited interfaces, so `AsImplementedInterfaces()` finds these too — called out because it is a subtler case than plain naming mismatch. ASP.NET Core also resolves `IEnumerable<IAuthorizationHandler>` and runs every registered handler, so both registrations of this interface are by-design, not a last-registration-wins conflict — scanning must register both, not deduplicate to one.
- `IAuthorizationMiddlewareResultHandler` → `RecentAuthMiddlewareResultHandler` (line 241)

**Exact non-scannable-within-the-84 list, all 5** (verified by parsing every `builder.Services.Add{Scoped,Singleton,Transient}` call site programmatically and checking each one's enclosing `if (builder.Environment...)` scope — a single linear read-through has missed a conditional registration twice across this spec's revisions, so this list is parser-verified, not read-verified):
- `AddSingleton(TimeProvider.System)` (line 68) — an **instance registration**: no type parameter, a pre-built object handed directly to the container. No type-based scan rule can express this.
- `AddSingleton<IEmailService>(sp => new FileSinkEmailService(...))` (line 187) — the E2E branch of `IEmailService`'s three-way environment-conditional registration; a factory lambda, not a plain type pair.
- `AddSingleton<IEmailService, LogOnlyEmailService>()` (line 199) — the no-API-key-non-prod branch of the same conditional.
- `AddScoped<IEmailService, ResendEmailService>()` (line 212) — the production branch of the same conditional. (All three `IEmailService` branches are mutually exclusive at runtime — exactly one executes — but each is its own source-line call site, hence 3 separate entries here rather than "1 conditional block.")
- `AddSingleton<IBreachedPasswordChecker, AlwaysAllowBreachedPasswordChecker>()` (line 245) — the E2E branch of `IBreachedPasswordChecker`'s two-way environment-conditional registration. **This conditional was missed by the original `ceres-researcher` dispatch (which stated no conditional registration exists) AND by two earlier drafts of this section within this same review** — found only by parsing every `Add{Scoped,Singleton,Transient}` call's enclosing scope programmatically rather than reading the file once. Its non-E2E sibling, `AddHttpClient<IBreachedPasswordChecker, HaveIBeenPwnedPasswordChecker>()` (line 249), uses a distinct extension method outside the 84-call scope entirely (see below) — it is not a sixth item in this list, only context for why the E2E branch's sibling isn't itself one of the 84.

**Separately — non-scannable, but never part of the 84-call figure at all** (a distinct extension-method family: `AddDbContext`, `Configure<TOptions>`, `AddOptions<TOptions>`, `AddHttpClient`; listed here for completeness of "what stays manual," not folded into the 79+5=84 arithmetic above):
- `AddDbContext<AppDbContext>` (line 86), `AddDbContext<AdminDbContext>` (line 96) — factory-configured, take a `(sp, options) =>` lambda Scrutor cannot express.
- `Configure<Argon2idOptions>` (104), `Configure<TokenLookupOptions>` (110), `AddOptions<LockoutCacheOptions>()` (168), `Configure<EmailOptions>` (176), `Configure<SecurityStampValidatorOptions>` (148), `Configure<CookieAuthenticationOptions>` (284), `Configure<MvcOptions>` (306), `Configure<FileAttachmentOptions>` (573) — options binders, not services.
- `AddHttpClient<IBreachedPasswordChecker, HaveIBeenPwnedPasswordChecker>()` (249) — a typed-client registration, the production-environment sibling of the `AlwaysAllowBreachedPasswordChecker` E2E branch listed above.

**DI-ordering check (verify-against-codebase backend pre-flight, step 3):** confirmed no registration in `Program.cs` depends on *source-code order* within `builder.Services`. `sp.GetRequiredService<...>()` calls inside factory lambdas (e.g. `AddDbContext`'s interceptor resolution at lines 89–90, 99) resolve against the fully-built container at request time, not against registration order — standard ASP.NET Core DI behavior. `InvalidModelStateResponseFactory` (line 34) is `ApiBehaviorOptions` pipeline configuration, unrelated to service registration, and precedes all service registrations regardless. **A scan (which registers in assembly-metadata order, not source order) introduces no new ordering risk beyond what already exists today.**

## 3a. Scan-mechanism constraints (added on second review — READ before implementation)

Scrutor's `AsImplementedInterfaces()` registers a class against **every** interface `Type.GetInterfaces()` returns for it — declared or inherited, project-defined or framework — not just "the one interface the developer meant." This is a documented, real-world failure mode in other projects: FluentValidation's `AbstractValidator<T>` subclasses, scanned the same way, ended up registered against `IEnumerable`, `IValidationRule`, and `IValidator` in addition to the intended `IValidator<T>`, because the base class incidentally implements those too.

**Checked whether this is live in Project Ceres today:** every class in the three scan-target namespaces (`ProjectCeres.Services`, `ProjectCeres.Admin`, `ProjectCeres.Common.Authentication`) was swept for `IDisposable`/`IAsyncDisposable` — the most common incidental-interface case. None of the 84 registered services implement either (`PreAuthRlsScope.cs` has an `IAsyncDisposable` type, but it is a helper struct, not one of the registered services). **Not a live risk today.**

This is a standing constraint for future work, not a defect to fix now: any class added later to the three scan namespaces that implements more than its one intended project interface (most commonly by adding `IDisposable`, or by inheriting a base class that implements something incidental) needs a deliberate look before assuming the scan does the right thing — the §8 D5 completeness check proves every dependency CAN resolve; it does NOT prove a service wasn't ALSO registered against an interface nobody wanted it registered against. If this ever becomes a real problem, Scrutor's `AsImplementedInterfaces(Func<Type, bool> predicate)` overload (confirmed via the same research pass, §4) filters which interfaces get registered — the fix is available if needed, just not applied preemptively here since nothing in the codebase needs it today.

## 4. What Scrutor is (verified, not assumed)

Confirmed via web search 2026-09-27 (not merely general model familiarity, per this project's research-before-confident-claims rule):
- **Current version: 7.0.0**, released 2025-11-24. MIT-licensed. Actively maintained (khellang/Scrutor on GitHub).
- **Targets .NET 10 explicitly** — v7.0.0's own changelog lists "Update to .NET 10" and requires `Microsoft.Extensions.DependencyInjection.Abstractions >= 10.0.0`. This project runs .NET 10 (CLAUDE.md § Tech Stack) — version floor is satisfied with no forced downgrade.
- **API surface** (confirmed via the Code Maze walkthrough cross-referenced against the package's documented usage): `services.Scan(scan => scan.FromAssemblyOf<T>().AddClasses(classes => classes.InNamespaces(...)).AsImplementedInterfaces().WithScopedLifetime())` is the real, current shape. `AsSelf()` (for D2's concrete-only classes) and `AsImplementedInterfaces()` (for D1's interface pairs) are both real, documented methods on the same fluent builder.
- v7.0.0 added keyed-service registration support — not needed by this stage (the codebase has no keyed services today, confirmed by `ceres-researcher`), noted only so a future stage knows the capability exists if keyed services are ever introduced.

No existing `Microsoft.Extensions.DependencyInjection.Abstractions` pin exists in `ProjectCeres.csproj` today (it flows transitively from the ASP.NET Core shared framework reference) — adding the `Scrutor` `PackageReference` is the first explicit dependency in this space; no version conflict is anticipated given the floor above, but this should be re-confirmed at implementation time via `dotnet restore` succeeding cleanly.

## 5. Existing conventions confirmed unaffected (ceres-researcher findings, verified)

- **`[RequiresAdminContext]` + `AdminContextDisciplineTests`**: the attribute is a plain marker on controller actions/methods, discovered via `Assembly.GetTypes()`-style reflection over attribute-decorated methods. It never inspects `Program.cs` source text or the resolved `IServiceProvider`'s registration list. **Unaffected by this stage.**
- **No custom Roslyn analyzer under `ProjectCeres.Analyzers/`** inspects `Program.cs`, DI registration patterns, or constructor-injection shapes. **Unaffected.**
- **No architecture test enumerates or greps `Program.cs`'s registration lines.** Existing tests are mechanism-agnostic — they test what's resolvable from a built `IServiceProvider`, not how it got registered. **Unaffected.**
- **Every `WebApplicationFactory`/`TestWebApplicationFactory`-based integration test boots the full DI graph from `Program.cs`.** A scan miswiring (wrong lifetime, wrong namespace boundary, accidental double-registration) would surface as failures scattered across the integration suite rather than localized to one test — this is why D5's dedicated completeness check exists: to localize a scan mistake to one fast, clearly-named failure instead of a diffuse scatter of unrelated-looking test failures.

## 6. Components

| Unit | Responsibility | New/reuse |
|---|---|---|
| `Scrutor` NuGet package (`ProjectCeres.csproj`) | The scanning library itself | New dependency |
| `Program.cs` `Scan(...)` call(s) | Two scan rules, both scoped to the three known service namespaces (`ProjectCeres.Services`, `ProjectCeres.Admin`, `ProjectCeres.Common.Authentication`): (a) any class implementing at least one interface — declared or inherited, project-defined or framework (§2 D1a; naming symmetry is NOT the rule) → `AsImplementedInterfaces().WithScopedLifetime()`; (b) any class in the same namespaces implementing no interface at all → `AsSelf().WithScopedLifetime()` | New, replaces 79 manual lines (§3) |
| `[RegisterAsSingleton]` marker attribute | Escape hatch for the minority of scanned classes needing `Singleton` lifetime instead of the D4 default `Scoped` | New, small (`ProjectCeres/Common/RegisterAsSingletonAttribute.cs` or similar — exact location decided at plan time) |
| DI-completeness check | Walks every constructor requiring a service and confirms the container can resolve it; runs at real app startup AND as a fast test | New — the D5 safety net |
| Updated `docs/architecture.md` / project DI convention doc | States the naming/namespace convention new services must follow to be picked up by the scan, and explicitly lists the manual-registration boundary (§3's true-non-scannable list) so it doesn't silently grow undocumented | Doc update |
| Updated `IUserOwned` 5-registry checklist (wherever it's documented — the `feedback_iuserowned_requires_five_registries` memory + any doc it points to) | DI step changes from "add a line to `Program.cs`" to "name the class/interface per convention; the scan + completeness check cover it" | Doc update, not a silent deletion |

## 7. Migration approach

This is a mechanical, high-blast-radius, low-novelty change (every integration test boots the DI graph, per §5) — not a redesign of any service's behavior. The migration:

1. Add the Scrutor package reference.
2. Add the `[RegisterAsSingleton]` attribute type.
3. Write the two `Scan(...)` calls in `Program.cs`, scoped to the three known namespaces.
4. Write the D5 completeness check (both the startup call and its test-suite equivalent).
5. **Remove the 79 manual lines the scan now covers** (§3: 37 + 5 + 1 + 36), one namespace/category at a time, running the full test suite after each removal batch to localize any miswiring immediately rather than removing all 79 lines in one commit and debugging a scattered failure set.
6. Leave the 5 non-scannable-within-the-84 lines and the separate `AddDbContext`/`Configure`/`AddOptions`/`AddHttpClient` lines (§3's exact lists) untouched.
7. Update the two docs in §6's last row.

Step 5's incremental-removal-with-test-runs-between-batches is the concrete answer to §5's "scan miswiring surfaces as scattered failures" risk — it bounds the blast radius of any one commit to a known, small batch rather than the whole 79-line set at once.

## 8. Testing (ship-gate)

- D5's completeness check: a unit/integration test that walks every constructor parameter type in every controller/service class discovered by reflection and asserts the DI container can resolve it — this is the check that would have caught, at zero runtime cost, any of the 79 migrated registrations landing wrong.
- Full existing integration suite must stay green after each incremental-removal batch (step 5) — this stage adds no new *behavior*, so no new business-logic test coverage is expected; the existing suite booting successfully via `WebApplicationFactory` on every affected service IS the regression test.
- A negative test: a class placed OUTSIDE the three scanned namespaces (in the test project only, never committed to `ProjectCeres/`) should NOT be picked up regardless of whether it implements an interface — proves the scan's namespace boundary is real, not accidentally matching everything in the assembly. (Per §2 D1a, an in-namespace class is picked up regardless of naming, so the negative test's boundary is namespace membership, not name pattern.)
- Confirm `[RegisterAsSingleton]` actually changes lifetime for at least one real case (pick one of the two existing `AddSingleton<T>` concrete-class registrations, e.g. `TokenLookupHasher` or `ErasurePseudonym`, as the worked example the plan converts first).

## 9. Out of scope (deferred)

These are features Scrutor has that this stage does not use — a different concern from §3a's scan-mechanism constraint, which is a risk inherent to the feature this stage DOES use (`AsImplementedInterfaces()`):

- Decorator-pattern registrations (`Scrutor.Decorate(...)`) — the codebase has none today (confirmed by `ceres-researcher`); not needed by this stage.
- Keyed-service registration (Scrutor v7.0.0 supports it) — the codebase has no keyed services today; not needed by this stage.
- Any change to the `IEmailService` environment-conditional registration's *logic* — it stays exactly as-is, only confirmed (§3) as correctly out of scan-scope.
- A NuGet-side pinning ADR — not needed unless a future Scrutor dependency-tree conflict forces one (§2 D1 note).

## 10. Docs to sync

**At spec creation, same commit window as this file:** every new stage — spec'd or not, planned or not — gets logged in the corresponding roadmap file up front, per standing project convention (precedent: commit `755b678e`, "roadmap — add §12.13 CI stage", added the same day the stage was scoped, well before its close-out). `docs/roadmap-phase-three.md` already has a standalone `## Stage 13.a` top-level section (added alongside this spec, between `## Stage 13` and `## Stage 14`, matching the `## Stage 12.13`–`12.19` pattern — NOT a row in Stage 13's own `### Sub-stages` table) pointing back at this file.

**On implementation completion:**
- `docs/architecture.md` — document the scan convention (namespace membership + interface-implementation structure, NOT a naming pattern — §2 D1a) as the new "how a service gets wired up" story, replacing/supplementing whatever it currently says (verified in research: currently silent on this, a doctrinal gap, not a contradiction to resolve).
- The `IUserOwned` 5-registry documentation — reword the DI step per §6's last row.
- `docs/roadmap-phase-three.md` — tick Stage 13.a's verification checklist items (added by the implementation plan) and mark the stage done, per the same close-out flow every other stage follows (CLAUDE.md § After Completing Any Stage).
