# Stage 13.a — Scrutor Assembly-Scanning DI Registration (Design Spec)

> **Diataxis type:** Reference — design for a new sub-stage in the Stage 13 family, consumed by `superpowers:writing-plans` next.
>
> **Stage id:** `13.a`. Deliberately letter-suffixed, not `13.12`: Stage 13's numeric sub-stages (13.1–13.11) are all GDPR-baseline items tracked against `legal.md`/`security-model.md`; this stage is an unrelated infrastructure change (how services get wired into DI) that the user asked to log inside the Stage 13 family rather than the Stage 12 infra family, without colliding with an existing GDPR sub-stage number. `13.1` is taken ("Privacy policy text + page") — confirmed by reading `docs/roadmap-phase-three.md` line 1818 before choosing this id.

---

## 1. Purpose

Replace `ProjectCeres/Program.cs`'s manually-written interface-to-implementation service registrations with [Scrutor](https://github.com/khellang/Scrutor) assembly scanning, so adding a new service no longer requires a hand-written `builder.Services.AddScoped<IFoo, Foo>()` line to be remembered. This also removes the DI-registration step from the project's `IUserOwned` "5-registry" checklist for new user-owned entities, per the user's explicit choice (§2 D1).

## 2. Decisions locked in brainstorming (2026-09-27)

| # | Decision | Rationale |
|---|---|---|
| D1 | **Full scan, no exceptions**, for everything that has an interface-to-class OR a bare-concrete-class shape. | User's explicit choice over a partial-adoption option that would have kept the `IUserOwned` checklist's DI line manual. Confirmed twice — once before the concrete-class-count correction (§3), once after. |
| D2 | **Self-registered concrete classes (no interface) are IN scope** — scanned via a namespace + naming-convention rule, not left manual. | The initial brainstorm draft assumed these ~24 classes (`ErasureExecutor`, `PasswordResetService`, `AdminRoleService`, the report generators, etc.) belonged in the same manual bucket as true non-scannables. Counting the actual file (§3) showed they are a distinct, scannable category with no interface to match against — user chose to include them once the distinction was surfaced, keeping "full scan, no exceptions" meaningful. |
| D3 | **True non-scannables stay manual, named explicitly as out-of-policy-scope, not as an exception to D1.** | These have no interface-to-class shape at all (a `DbContext`, an `Options` binder, a typed `HttpClient`, an environment-conditional branch) — Scrutor's convention scan has nothing to match against. The spec states this so it reads as "never in scope" rather than "scope creep back toward partial adoption." |
| D4 | **Default lifetime for scanned registrations is `Scoped`**, with a `[RegisterAsSingleton]` marker attribute as the documented escape hatch for the minority that need `Singleton`. | Matches the overwhelming majority of today's registrations (verified in §3). A per-class attribute is simpler than a second naming convention and is greppable. |
| D5 | **A startup-time DI-completeness check is mandatory**, run both at real app boot and as a fast automated test. | Full-scan adoption converts a compile-time-adjacent mistake (forgetting a line) into a silent scan-miss (wrong name, wrong namespace, missing the naming pattern) that would otherwise only surface as a runtime `null`/resolution exception far from the actual mistake. User chose this over "rely on the existing test suite to catch it eventually." |

No ADR is required for the Scrutor package addition itself — this project has no NuGet-side pinning-ADR convention (confirmed absent, `docs/decisions/` searched; only the pnpm-side ADR-0079 exists) and Scrutor introduces no transitive-advisory conflict to document. If a future NuGet override is ever needed for Scrutor's own dependency tree, that is the trigger for originating the first NuGet-side ADR — not this stage.

## 3. Deviations from the initial brainstorm draft (verify-against-codebase + direct file count — READ)

The brainstorm's first pass, based on `ceres-researcher`'s dispatch, characterized `Program.cs`'s registrations as roughly "~50 interface pairs plus a smaller set of concrete-only/options/factory registrations," and initially treated all concrete-only registrations as one non-scannable bucket. Counting the actual file (`grep -c` + manual read of every match) found a materially different, three-way split:

| Category | Count | Scan treatment |
|---|---|---|
| Interface-to-class pairs (`AddScoped<IFoo, Foo>()`-shaped) | **49** | Scanned — the original convention rule |
| Self-registered concrete classes (no interface; registered by own type) | **~24** | **Scanned** (D2) — a second convention rule, by namespace |
| True non-scannables (DB contexts, `Configure<TOptions>` binders, typed `HttpClient`, environment-conditional branches) | **~12** (see exact list below) | Stays manual (D3) |

Total `Add{Scoped,Singleton,Transient}` call sites in `Program.cs`: **84**. The 49+~24 ≈ 73 that fall under D1/D2 is the real scope of "full scan, no exceptions" — not literally 84, and not literally "everything."

**Exact true-non-scannable list** (verified by reading each, `Program.cs` line numbers as of commit `9c66d190`):
- `AddDbContext<AppDbContext>` (line 86), `AddDbContext<AdminDbContext>` (line 96) — factory-configured, take a `(sp, options) =>` lambda Scrutor cannot express.
- `Configure<Argon2idOptions>` (104), `Configure<TokenLookupOptions>` (110), `AddOptions<LockoutCacheOptions>()` (168), `Configure<EmailOptions>` (176), `Configure<CookieAuthenticationOptions>` (284), `Configure<MvcOptions>` (306), `Configure<FileAttachmentOptions>` (573), `Configure<SecurityStampValidatorOptions>` (148) — options binders, not services.
- `AddHttpClient<IBreachedPasswordChecker, HaveIBeenPwnedPasswordChecker>()` (249) — typed-client registration, a distinct ASP.NET Core DI extension point Scrutor does not scan.
- **`IEmailService`'s three-way environment-conditional registration** (lines 180–213): E2E → `FileSinkEmailService` (factory lambda), no-API-key-non-prod → `LogOnlyEmailService`, else → `ResendEmailService`. This is a genuine interface-pair shape (`IEmailService, ResendEmailService`) in its third branch, but the *choice* of which implementation wins is runtime-environment-conditional — Scrutor's naming-convention scan has no "only in this environment" predicate. **This finding was NOT surfaced by the initial `ceres-researcher` dispatch**, which stated "no environment-gated conditional registration" exists in the codebase; the `verify-against-codebase` pre-flight (§ backend step 3, DI-registration-order check) found it by reading `Program.cs` directly. Stays manual, all three branches, under D3.
- `AddSingleton<IAuthorizationHandler, RecentAuthRequirementHandler>()` (239) + `AddSingleton<IAuthorizationHandler, AdminLiveRequirementHandler>()` (240) — **not actually a non-scannable**, included here only to close the loop: ASP.NET Core resolves `IEnumerable<IAuthorizationHandler>` and runs all registered handlers, so multiple registrations of this interface are by-design, not a last-registration-wins conflict. Both ARE in scope for D1's interface-pair scan (each is a normal one-to-one `IAuthorizationHandler` implementation); scanning must register both, not deduplicate to one.

**DI-ordering check (verify-against-codebase backend pre-flight, step 3):** confirmed no registration in `Program.cs` depends on *source-code order* within `builder.Services`. `sp.GetRequiredService<...>()` calls inside factory lambdas (e.g. `AddDbContext`'s interceptor resolution at lines 89–90, 99) resolve against the fully-built container at request time, not against registration order — standard ASP.NET Core DI behavior. `InvalidModelStateResponseFactory` (line 34) is `ApiBehaviorOptions` pipeline configuration, unrelated to service registration, and precedes all service registrations regardless. **A scan (which registers in assembly-metadata order, not source order) introduces no new ordering risk beyond what already exists today.**

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
| `Program.cs` `Scan(...)` call(s) | Two scan rules: (a) interface-pair classes → `AsImplementedInterfaces().WithScopedLifetime()`, scoped to the three known service namespaces (`ProjectCeres.Services`, `ProjectCeres.Admin`, `ProjectCeres.Common.Authentication`); (b) self-registered concrete classes (no matching interface) in the same namespaces → `AsSelf().WithScopedLifetime()` | New, replaces ~73 manual lines |
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
5. **Remove the ~73 manual lines the scan now covers**, one namespace/category at a time, running the full test suite after each removal batch to localize any miswiring immediately rather than removing all 73 lines in one commit and debugging a scattered failure set.
6. Leave the ~12 true-non-scannable lines (§3's exact list) untouched.
7. Update the two docs in §6's last row.

Step 5's incremental-removal-with-test-runs-between-batches is the concrete answer to §5's "scan miswiring surfaces as scattered failures" risk — it bounds the blast radius of any one commit to a known, small batch rather than the whole 73-line set at once.

## 8. Testing (ship-gate)

- D5's completeness check: a unit/integration test that walks every constructor parameter type in every controller/service class discovered by reflection and asserts the DI container can resolve it — this is the check that would have caught, at zero runtime cost, any of the 73 migrated registrations landing wrong.
- Full existing integration suite must stay green after each incremental-removal batch (step 5) — this stage adds no new *behavior*, so no new business-logic test coverage is expected; the existing suite booting successfully via `WebApplicationFactory` on every affected service IS the regression test.
- A negative test: a class deliberately named to NOT match either scan convention (in the test project only, never committed to `ProjectCeres/`) should NOT be picked up — proves the scan's namespace/naming boundary is real, not accidentally matching everything in the assembly.
- Confirm `[RegisterAsSingleton]` actually changes lifetime for at least one real case (pick one of the two existing `AddSingleton<T>` concrete-class registrations, e.g. `TokenLookupHasher` or `ErasurePseudonym`, as the worked example the plan converts first).

## 9. Out of scope (deferred)

- Decorator-pattern registrations (`Scrutor.Decorate(...)`) — the codebase has none today (confirmed by `ceres-researcher`); not needed by this stage.
- Keyed-service registration (Scrutor v7.0.0 supports it) — the codebase has no keyed services today; not needed by this stage.
- Any change to the `IEmailService` environment-conditional registration's *logic* — it stays exactly as-is, only confirmed (§3) as correctly out of scan-scope.
- A NuGet-side pinning ADR — not needed unless a future Scrutor dependency-tree conflict forces one (§2 D1 note).

## 10. Docs to sync on completion

- `docs/architecture.md` — document the scan convention (namespace + naming pattern) as the new "how a service gets wired up" story, replacing/supplementing whatever it currently says (verified in research: currently silent on this, a doctrinal gap, not a contradiction to resolve).
- The `IUserOwned` 5-registry documentation — reword the DI step per §6's last row.
- `docs/roadmap-phase-three.md` — add Stage 13.a to the sub-stage table (or an equivalent location outside the 13.1–13.11 numeric sequence, per the stage-id decision in this doc's header) once the plan is approved and work begins.
