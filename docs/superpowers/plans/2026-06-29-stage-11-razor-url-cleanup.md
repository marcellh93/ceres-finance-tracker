# Stage 11 — Razor + URL cleanup — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Project Ceres a pure Web API + SPA — drop the `/app/` URL prefix, strip MVC/Razor from `Program.cs`, delete all Razor controllers + `Views/`, shelve import + Review from the beta (recoverable), and drive `pnpm lint` to zero.

**Architecture:** Three dependency-ordered commits. (1) Shelve import + Review — independent, reversible. (2) Go pure-SPA — serve the already-built `dist/app.html` via `MapFallbackToFile`, flip the React Router basename, delete Razor controllers/views, strip MVC from `Program.cs`. (3) Re-tighten the two narrowed architecture tests and clear the 39-item lint baseline.

**Tech Stack:** ASP.NET Core 10 (`Program.cs` minimal-hosting), `Vite.AspNetCore` 2.4.1 (manifest mode), React 19 + React Router + Vite, xUnit + FluentAssertions, Vitest, Playwright (e2e).

## Global Constraints

- **Recoverable, not deleted:** import + Review code, services, and `ImportStaged*` tables stay in the tree (ADR-0078). Only nav/routes/host change; endpoints are fenced, not removed.
- **No weakened/deleted tests:** per `docs/testing.md` § Rules. Client unit tests (mocked) keep passing as-is. Server integration tests stay live by running in a non-beta test environment; a NEW architecture test pins the beta-environment 404 separately.
- **No bare lint suppressions:** every `eslint-disable` carries a `// ... — Why: <reason>` comment.
- **Frontend commands:** `pnpm --dir ProjectCeres.Client …`, never `cd`-then-`pnpm`, never `npm`.
- **Commit messages** end with: `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- **The SPA host is `dist/app.html`** (the `app.html` rollup input → `src/app/main.tsx`), NOT `index.html` (the design-system entry → `src/main.tsx`).
- **Stop hook runs `dotnet test`** on turn-end when `.cs` is touched — do NOT also run it in-turn (test-DB collision). Let the hook run it.

---

## COMMIT 1 — Shelve import + Review

Independent of routing. Lands first. Fully reversible.

### Task 1: Remove Import + Review nav items

**Files:**
- Modify: `ProjectCeres.Client/src/app/layout/nav-items.ts`
- Test: `ProjectCeres.Client/src/app/layout/Sidebar.test.tsx`, `TopBar.test.tsx`, `AppLayout.a11y.test.tsx`

**Interfaces:**
- Produces: `navGroups` with no Import/Review entries; `bottomItems` unchanged. No `useReviewBadge`/`useReviewCount` export.

- [ ] **Step 1: Update the Sidebar test to assert Import + Review are absent**

In `Sidebar.test.tsx`, find the test(s) asserting nav labels render. Add/adjust an assertion:

```tsx
it('does not render shelved Import or Review nav items', () => {
  render(<Sidebar /* existing props */ />, { wrapper: /* existing wrapper */ });
  expect(screen.queryByRole('link', { name: /import/i })).not.toBeInTheDocument();
  expect(screen.queryByRole('link', { name: /review/i })).not.toBeInTheDocument();
});
```

Remove any existing assertion that expects `Import` or `Review` links to be present.

- [ ] **Step 2: Run the test, verify it fails**

Run: `pnpm --dir ProjectCeres.Client test --run src/app/layout/Sidebar.test.tsx`
Expected: FAIL — Import/Review links still present.

- [ ] **Step 3: Edit `nav-items.ts`**

Remove the `useReviewCount` import (line 14), the `useReviewBadge` hook (line 36), the Review item from the `Activity` group, and the Import item from the `Tools` group. Remove now-unused icon imports `Inbox` (line 3) and `Upload` (line 10).

Resulting `navGroups`:

```ts
export const navGroups: NavGroup[] = [
  {
    label: 'Activity',
    items: [
      { to: '/movements', label: 'Movements', icon: LayoutList },
    ],
  },
  {
    label: 'Money',
    items: [
      { to: '/accounts',   label: 'Accounts',   icon: Landmark },
      { to: '/categories', label: 'Categories', icon: Tags },
      { to: '/budgets',    label: 'Budgets',    icon: Wallet },
    ],
  },
  {
    label: 'Tools',
    items: [
      { to: '/recurring', label: 'Recurring Transactions', icon: Repeat },
      { to: '/reports',   label: 'Reports',                 icon: BarChart3 },
    ],
  },
];
```

Remove the unused `Inbox`, `Upload` from the lucide import block.

- [ ] **Step 4: Run all three layout tests, verify pass**

Run: `pnpm --dir ProjectCeres.Client test --run src/app/layout/`
Expected: PASS. (If `TopBar.test.tsx` / `AppLayout.a11y.test.tsx` assert on Import/Review presence, update those assertions to absence in the same step.)

- [ ] **Step 5: Commit** (defer to Task 4 — one commit for all of Commit 1)

### Task 2: Remove import + Review routes and the ReviewCountProvider mount

**Files:**
- Modify: `ProjectCeres.Client/src/app/App.tsx` (imports + route table)
- Modify: `ProjectCeres.Client/src/app/layout/AppLayout.tsx` (provider tree)
- Test: `ProjectCeres.Client/src/app/App.test.tsx` (or the route-resolution test if one exists), `AppLayout.a11y.test.tsx`

**Interfaces:**
- Consumes: `NotFound` page (already imported, line 12).
- Produces: `/import`, `/import/profiles`, `/review` resolve to `NotFound`; no `ReviewCountProvider` in the tree.

- [ ] **Step 1: Write a failing test that `/review` and `/import` render NotFound**

In `App.test.tsx` (create the test if the file lacks route-resolution coverage; follow the existing render-with-MemoryRouter pattern):

```tsx
it.each(['/review', '/import', '/import/profiles'])(
  'renders NotFound for shelved route %s',
  (path) => {
    render(
      <MemoryRouter initialEntries={[path]}>
        <App />
      </MemoryRouter>,
    );
    expect(screen.getByText(/not found/i)).toBeInTheDocument();
  },
);
```

- [ ] **Step 2: Run it, verify it fails**

Run: `pnpm --dir ProjectCeres.Client test --run src/app/App.test.tsx`
Expected: FAIL — routes still resolve to Review/Import pages.

- [ ] **Step 3: Edit `App.tsx`**

Remove these imports: `Import` (line 11), `Review` (line 24), `ProfilesLayout`/`ProfileCreate`/`ProfileEdit` (lines 38–40). Remove the `<Route>` entries for `/import`, `/import/profiles` (and nested profile routes), and `/review`. Leave the catch-all `<Route path="*" element={<NotFound />} />` so those paths fall through to NotFound.

- [ ] **Step 4: Edit `AppLayout.tsx`**

Remove the `ReviewCountProvider` import and unwrap it from the provider tree (children render directly where it previously wrapped them). Confirm no other consumer of `useReviewCount` remains outside the review feature folder:

Run: `grep -rn "useReviewCount" ProjectCeres.Client/src/app --include=*.tsx --include=*.ts | grep -v features/review`
Expected: no output.

- [ ] **Step 5: Update `AppLayout.a11y.test.tsx`**

If it renders `ReviewCountProvider` or asserts the Review nav/badge, remove those — render `AppLayout` without the provider.

- [ ] **Step 6: Run tests, verify pass**

Run: `pnpm --dir ProjectCeres.Client test --run src/app/App.test.tsx src/app/layout/`
Expected: PASS.

NOTE: the review feature's own unit tests (`features/review/*.test.tsx`) and import feature unit tests (`features/import/*.test.tsx`) are MOCKED (vi.mock / MemoryRouter) and test components that remain in the tree — leave them untouched, they still pass and still cover retained code.

- [ ] **Step 7: Commit** (defer to Task 4)

### Task 3: Fence the import + Review API endpoints to a non-beta environment

**Files:**
- Modify: `ProjectCeres/Program.cs` (conditional controller-feature registration OR an endpoint convention)
- Create: `ProjectCeres.Tests/Integration/Authentication/ShelvedEndpointFencingTests.cs`
- Modify (env-aware): the WAF test fixture's environment so existing import/review integration tests stay live.

**Interfaces:**
- Consumes: `builder.Environment.IsEnvironment(...)` (in-tree idiom, used 7× in Program.cs).
- Produces: in the **beta** environment (Production / default), routes `api/import`, `api/import/headers`, `api/import-profiles`, `api/reconciliation-review`, `api/transfer-review` return 404. In the **test/E2E** environment they remain registered.

- [ ] **Step 1: Write the failing fencing test (beta env → 404)**

```csharp
// ShelvedEndpointFencingTests.cs
public class ShelvedEndpointFencingTests
{
    [Theory]
    [InlineData("/api/import")]
    [InlineData("/api/import/headers")]
    [InlineData("/api/import-profiles")]
    [InlineData("/api/reconciliation-review")]
    [InlineData("/api/transfer-review")]
    public async Task Shelved_import_and_review_endpoints_return_404_in_beta(string path)
    {
        // Factory configured WITHOUT the test/E2E environment opt-in → beta posture.
        using var factory = new AuthTestWebApplicationFactory()
            .WithEnvironment("Production");
        var client = factory.CreateClient();
        var resp = await client.GetAsync(path);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "import + Review are shelved from the beta (ADR-0078); endpoints must be unreachable outside a non-beta environment");
    }
}
```

(If `AuthTestWebApplicationFactory` lacks a `.WithEnvironment(...)` hook, use `WebApplicationFactory<Program>.WithWebHostBuilder(b => b.UseEnvironment("Production"))` and reconfigure the test DB connection as the existing factory does.)

- [ ] **Step 2: Run it, verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ShelvedEndpointFencingTests"`
Expected: FAIL — endpoints currently return 200/401, not 404.

- [ ] **Step 3: Implement the fence in `Program.cs`**

Register the five shelved controllers conditionally. Use an `ApplicationPart`/feature-provider exclusion or a controller-convention guard keyed on environment. Minimal shape — remove the shelved controllers from the controller feature unless the environment is test/E2E:

```csharp
// after AddControllers()
var importAndReviewEnabled = builder.Environment.IsEnvironment("E2E")
    || builder.Environment.IsEnvironment("Testing");
if (!importAndReviewEnabled)
{
    builder.Services.AddControllers(options =>
        options.Conventions.Add(new ShelvedControllerRouteSuppressionConvention(
            typeof(ImportApiController),
            typeof(ImportHeadersController),
            typeof(ImportProfilesApiController),
            typeof(ReconciliationReviewApiController),
            typeof(TransferReviewApiController))));
}
```

Implement `ShelvedControllerRouteSuppressionConvention : IApplicationModelConvention` to clear `Selectors` (removes routing) for the listed controller types — they remain instantiable (recoverable) but unrouted. Verify the exact convention API against the ASP.NET version before finalizing; if a feature-provider exclusion is cleaner in-tree, use that instead — the test pins the behavior either way.

- [ ] **Step 4: Make the existing import/review integration tests run in the enabled environment**

The WAF fixture for `ImportApiTests`, `ReconciliationReviewApiTests`, `TransferReviewApiTests`, `ImportProfilesCrudApiTests` must run under `E2E`/`Testing` so endpoints stay registered. Confirm the fixture's `UseEnvironment(...)` already sets one of those (the memory note says the WAF routes through `ceres_admin` BYPASSRLS — check the env). If not, set it.

Run: `dotnet test --filter "FullyQualifiedName~ImportApiTests|FullyQualifiedName~ReconciliationReviewApiTests|FullyQualifiedName~TransferReviewApiTests"`
Expected: PASS (endpoints live in test env).

- [ ] **Step 5: Run the fencing test, verify pass**

Run: `dotnet test --filter "FullyQualifiedName~ShelvedEndpointFencingTests"`
Expected: PASS.

- [ ] **Step 6: Commit** (defer to Task 4)

### Task 4: Correct ADR-0078 + sync docs, then commit all of Commit 1

**Files:**
- Modify: `docs/decisions/ADR-0078-import-shelved-from-phase-3-beta.md`
- Modify: `docs/api-contract.md`, `docs/models.md`, `docs/testing.md`
- Modify: `docs/roadmap-phase-three.md` (tick 11.9 checklist items now done)

- [ ] **Step 1: Correct ADR-0078**

Fix line 26 ("the Transfers tab is independent" — factually wrong). Replace with the resolved finding: both the Reconciliations and Transfers tabs read exclusively from `ImportStaged*` tables written only by `ImportService`; with import shelved, both go permanently empty, so the entire Review page (nav item, `/review` route, `ReviewCountProvider`) is shelved alongside import. Update the "Open question" section to record this resolution.

- [ ] **Step 2: Update api-contract.md / models.md / testing.md**

Note the import + reconciliation-review + transfer-review API as shelved-from-beta (fenced to non-beta env, code retained). In models.md note `ImportStaged*` staging entities are retained but their write path (import) is beta-disabled. In testing.md note the integration suites for these run only in the enabled environment.

- [ ] **Step 3: Tick the 11.9 roadmap checklist items** now satisfied (nav removed, routes 404, Review coupling resolved + recorded in ADR-0078, endpoints fenced + arch-test-pinned, code retained, docs updated, grep clean for nav/routes). Leave browser-only items unticked.

- [ ] **Step 4: Verify the client build + tests green**

Run: `pnpm --dir ProjectCeres.Client build && pnpm --dir ProjectCeres.Client test --run`
Expected: build within budgets; all tests pass.

- [ ] **Step 5: Commit Commit 1**

```bash
git add -A
git commit -m "feat(11.9): shelve import + Review from beta (ADR-0078)

Remove Import + Review nav items and /import, /import/profiles, /review
routes; unmount ReviewCountProvider. Fence the five import/review API
controllers to non-beta environments (404 in beta), pinned by
ShelvedEndpointFencingTests. Correct ADR-0078's wrong 'Transfers tab is
independent' premise — both Review tabs are import-fed. Code, services, and
ImportStaged* tables retained (recoverable).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## COMMIT 2 — Go pure-SPA

The load-bearing commit. The host re-homing is verified in isolation BEFORE controller deletions.

### Task 5: Re-home the SPA host — serve built `dist/app.html`, verify in isolation

**Files:**
- Modify: `ProjectCeres.Client/app.html` (port theme-flash script)
- Modify: `ProjectCeres.Client/vite.config.ts` (`base: '/dist/'`)
- Modify: `ProjectCeres/Program.cs` (`MapFallbackToFile("dist/app.html")`)
- Modify: `ProjectCeres/ProjectCeres.csproj` (guarded SPA build+copy target)
- Modify: `ProjectCeres.Client/e2e/auth/spa-stylesheet.spec.ts` (re-point URLs)

**Interfaces:**
- Produces: `app.MapFallbackToFile("dist/app.html")` serving the SPA at any unmatched path; built assets resolve at `/dist/assets/*`.

- [ ] **Step 1: Port the theme-flash inline script into `app.html`**

Copy the `ceres.theme:v1` `<script>` block from `index.html`'s `<head>` into `app.html`'s `<head>`, placed BEFORE the existing sidebar-collapsed script. Both inline scripts now live in `app.html`. (Vite passes inline `<script>` through untouched.)

- [ ] **Step 2: Set Vite base so asset URLs match the staged location**

In `vite.config.ts`, add `base: '/dist/'` to the config (top level, sibling of `build`). This makes emitted asset URLs `/dist/assets/*`, matching `wwwroot/dist/assets/*` served by `UseStaticFiles`.

- [ ] **Step 3: Build the SPA and stage it manually (mirrors run-server.sh step 5)**

Run:
```bash
pnpm --dir ProjectCeres.Client build
rm -rf ProjectCeres/wwwroot/dist && mkdir -p ProjectCeres/wwwroot/dist
cp -R ProjectCeres.Client/dist/. ProjectCeres/wwwroot/dist/
```
Expected: `ProjectCeres/wwwroot/dist/app.html` exists and references `/dist/assets/*.css` + `/dist/assets/*.js`.

Verify the host is self-contained:
```bash
grep -E 'rel="stylesheet"|type="module"' ProjectCeres/wwwroot/dist/app.html
```
Expected: at least one `<link rel="stylesheet" href="/dist/assets/...css">` and one `<script type="module" ... src="/dist/assets/...js">`.

- [ ] **Step 4: Add the `MapFallbackToFile` (temporarily alongside existing controllers)**

In `Program.cs`, after `app.MapControllers()` and the two `MapControllerRoute` calls (do NOT delete those yet — that's Task 8), add:

```csharp
app.MapFallbackToFile("dist/app.html");
```

- [ ] **Step 5: Add the guarded SPA build+copy MSBuild target**

In `ProjectCeres.csproj`, alongside the existing `BuildTailwind` target, add (paths verified disjoint — Tailwind writes `wwwroot/css/site.css`, this writes `wwwroot/dist/`):

```xml
<Target Name="BuildSpaClient" BeforeTargets="Build" Condition="'$(SkipSpaBuild)' != 'true' AND '$(Configuration)' == 'Release'">
  <Exec Command="pnpm install --frozen-lockfile" WorkingDirectory="$(ProjectDir)../ProjectCeres.Client" />
  <Exec Command="pnpm build" WorkingDirectory="$(ProjectDir)../ProjectCeres.Client" />
  <ItemGroup>
    <SpaDist Include="$(ProjectDir)../ProjectCeres.Client/dist/**/*" />
  </ItemGroup>
  <Copy SourceFiles="@(SpaDist)" DestinationFiles="@(SpaDist->'$(ProjectDir)wwwroot/dist/%(RecursiveDir)%(Filename)%(Extension)')" />
</Target>
```

`Condition` restricts it to `Release` + opt-out via `SkipSpaBuild`, so inner-loop `dotnet build`/`dotnet test` (Debug) never trigger the ~30–60s SPA build (avoids slowing the Tier-2 stop hook).

- [ ] **Step 6: Re-point the stylesheet e2e guard**

In `e2e/auth/spa-stylesheet.spec.ts`, change navigation URLs from `/app/login` → `/login` (and any `/app/*` → `/*`). Update the stale comment referencing `Views/App/Index.cshtml` to name the new host (`wwwroot/dist/app.html` served via `MapFallbackToFile`). The assertions (stylesheet `<link>` count, teal `--primary` computed bg) stay — Vite-emitted CSS still satisfies them.

- [ ] **Step 7: Verify the host in isolation — boot and check the stylesheet (the Stage-9 guard)**

Start the app in the manifest-serving environment:
```bash
ASPNETCORE_ENVIRONMENT=E2E dotnet run --project ProjectCeres &
# wait for boot, then:
curl -ks https://localhost:7081/ | grep -E 'rel="stylesheet"|id="root"'
```
Expected: the response is `dist/app.html` markup with a `<link rel="stylesheet" href="/dist/assets/...css">` and `<div id="root">`. If the stylesheet link is absent → STOP, the host is mis-served (do not proceed to deletions).

- [ ] **Step 8: Verify dev-mode serves the APP entry (the tech-lead's open question)**

Stop the E2E run. Start dev:
```bash
dotnet run --project ProjectCeres &
# then load https://localhost:7081/ in a browser (or curl the HTML)
curl -ks https://localhost:7081/ | grep -E 'src/app/main.tsx|src/main.tsx'
```
Expected: the served HTML references `src/app/main.tsx` (the APP entry), NOT `src/main.tsx` (the design-system shell). If it serves `src/main.tsx` or nothing, fix the dev branch in Task 7 to serve `app.html` before proceeding. Record the result inline in the plan checkbox.

- [ ] **Step 9: Commit checkpoint** (defer to Task 8 — Commit 2 is one commit, but this host work is verified-green before deletions land in the same commit)

### Task 6: Flip the React Router basename + sweep hard-coded `/app/` literals

**Files:**
- Modify: `ProjectCeres.Client/src/app/main.tsx` (basename)
- Modify: `ProjectCeres.Client/src/app/pages/Security.tsx:35`
- Modify: `ProjectCeres.Client/src/components/ui/navbar.tsx:48`

- [ ] **Step 1: Flip the basename**

In `main.tsx`, change `<BrowserRouter basename="/app">` → `<BrowserRouter basename="/">` (or remove the `basename` prop, since `/` is the default).

- [ ] **Step 2: Sweep the two hard-coded literals**

`Security.tsx:35` — `window.location.href = '/app/login'` → `'/login'`.
`navbar.tsx:48` — `href="/app/reports"` → `href="/reports"`.

- [ ] **Step 3: Grep for any remaining `/app/` in client source**

Run: `grep -rn "/app/" ProjectCeres.Client/src ProjectCeres.Client/*.html`
Expected: no output. (Investigate + fix any hit.)

- [ ] **Step 4: Run client tests**

Run: `pnpm --dir ProjectCeres.Client test --run`
Expected: PASS. (Tests using `MemoryRouter` are basename-agnostic; any test hard-coding `/app` prefixes gets updated here.)

- [ ] **Step 5: Commit** (defer to Task 8)

### Task 7: Add the `/app/*` → `/*` 301 + rewrite the Vite dev middleware

**Files:**
- Modify: `ProjectCeres/Program.cs` (add `UseRewriter`; rewrite the dev branch comment + confirm predicate)

- [ ] **Step 1: Add the 301 rewrite before the fallback**

In `Program.cs`, before `app.MapFallbackToFile(...)` (and after `UseRouting`/static files), add:

```csharp
app.UseRewriter(new RewriteOptions()
    .AddRedirect("^app/(.*)", "$1", statusCode: StatusCodes.Status301MovedPermanently));
```

Query string is auto-preserved by the rewrite middleware. This lives in the pipeline, NOT a controller (no MVC).

- [ ] **Step 2: Rewrite the dev-middleware comment block + fix any `/app/` coupling**

In the `if (app.Environment.IsDevelopment())` branch (~L603): the `IsViteRequest` predicate logic does NOT key on `/app/` (verified — only the comment does). Rewrite the comment block to remove `/app/`, `/app/login`, "owned by HomeController/AppController" references; state that page requests now fall through to `MapFallbackToFile("dist/app.html")` / the dev Vite server. If Task 5 Step 8 showed dev serves the wrong entry, adjust the dev branch so `UseViteDevelopmentServer` serves the `app.html` entry at `/`.

- [ ] **Step 3: Boot dev + E2E, confirm both still serve the app**

Run the Task 5 Step 7 (E2E) and Step 8 (dev) checks again.
Expected: both serve `dist/app.html` (prod) / `src/app/main.tsx` entry (dev), with the stylesheet present.

- [ ] **Step 4: Confirm the 301 works**

```bash
ASPNETCORE_ENVIRONMENT=E2E dotnet run --project ProjectCeres &
curl -ks -o /dev/null -w "%{http_code} %{redirect_url}\n" "https://localhost:7081/app/movements?needsReview=true"
```
Expected: `301 https://localhost:7081/movements?needsReview=true` (query preserved).

- [ ] **Step 5: Commit** (defer to Task 8)

### Task 8: Delete Razor controllers + Views; strip MVC from Program.cs

**Files:**
- Delete: all non-Api controllers under `ProjectCeres/Controllers/` (`AppController`, `HomeController`, and the 15 redirect stubs)
- Delete: `ProjectCeres/Views/` (entire directory, 8 `.cshtml`)
- Modify: `ProjectCeres/Program.cs` (remove `AddControllersWithViews` block + both `MapControllerRoute`)
- Delete: `ProjectCeres/Filters/NumberFormatActionFilter.cs` + `ProjectCeres/ModelBinders/DecimalModelBinderProvider.cs` (Razor-form-only; verified no API controller depends on the decimal binder — only local `decimal` vars in Dashboard/Reports)

- [ ] **Step 1: Delete the Razor controllers**

```bash
# Keep Controllers/Api/* ; delete everything else under Controllers/
find ProjectCeres/Controllers -maxdepth 1 -name '*.cs' -type f -delete
```
Verify: `grep -rn ": Controller" ProjectCeres/Controllers/ | grep -v /Api/` → no output.

- [ ] **Step 2: Delete Views**

```bash
rm -rf ProjectCeres/Views
```
Verify: `find ProjectCeres -name '*.cshtml'` → no output.

- [ ] **Step 3: Strip MVC from Program.cs**

Remove: the `AddScoped<NumberFormatActionFilter>()` line (L28), the entire `AddControllersWithViews(options => { ... })` block (L29–32, incl. the `Filters.AddService` + `ModelBinderProviders.Insert`), and both `MapControllerRoute` calls (the `app` route L669, the `default` route L673). KEEP `AddControllers().ConfigureApiBehaviorOptions(...)` (L35+, the 422 factory), `app.MapControllers()`, `UseStaticFiles()`, `AddViteServices()`, and `MapFallbackToFile("dist/app.html")`.

Also fix the `UseExceptionHandler("/Home/Error")` (L590) — `/Home/Error` no longer exists. Point it at a problem-details handler or the SPA fallback (`app.UseExceptionHandler(...)` with a JSON/ProblemDetails handler is the API-appropriate choice; confirm against `api-contract.md`).

- [ ] **Step 4: Delete the now-orphaned filter + model binder**

```bash
rm -f ProjectCeres/Filters/NumberFormatActionFilter.cs
# locate + remove the DecimalModelBinderProvider file
grep -rln "class DecimalModelBinderProvider" ProjectCeres/ | xargs rm -f
```
Verify the build doesn't reference them: handled by Step 5.

- [ ] **Step 5: Build the server**

Run: `dotnet build ProjectCeres/ProjectCeres.csproj`
Expected: 0 errors. (Any reference to deleted types surfaces here — fix.)

- [ ] **Step 6: Boot + smoke (E2E manifest mode)**

Run the Task 5 Step 7 stylesheet check + Task 7 Step 4 301 check again, now with NO controllers.
Expected: `/` serves the styled SPA; `/app/movements?...` 301s; `/Movements` (old Razor bookmark) → 404.

- [ ] **Step 7: Grep for `/app/` across the whole repo (code side)**

Run: `grep -rn "/app/" ProjectCeres ProjectCeres.Client/src --include=*.cs --include=*.ts --include=*.tsx`
Expected: no output. (The `UseRewriter` regex `"^app/(.*)"` has no leading slash, so it won't match this grep.)

- [ ] **Step 8: Commit Commit 2**

Let the Stop hook run the full `dotnet test` suite (this commit touches `.cs`). Then:

```bash
git add -A
git commit -m "feat(11): drop /app prefix, serve built SPA host, strip Razor/MVC

Serve dist/app.html via MapFallbackToFile (Vite manifest mode); flip Router
basename /app -> /; add /app/* -> /* 301 (RewriteOptions); rewrite the Vite
dev middleware. Delete all Razor controllers + Views/, the AddControllersWithViews
block, both MapControllerRoute calls, and the Razor-only NumberFormatActionFilter
+ DecimalModelBinderProvider. Pure Web API + SPA.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## COMMIT 3 — Re-tighten contracts + lint sweep

### Task 9: Widen the two narrowed architecture tests

**Files:**
- Modify: `ProjectCeres.Tests/Integration/Authentication/ArchitectureTests.cs:26-41`, `:60-87`

- [ ] **Step 1: Widen `No_api_controller_class_has_AllowAnonymous`**

Rename to `No_controller_class_has_AllowAnonymous`. Remove line 34 (`.Where(t => t.Namespace?.Contains(".Api") == true)`). Update the XML comment to drop the "legacy Razor controllers slated for deletion" rationale (they're gone). Update the assertion message to "controllers" not "API controllers".

- [ ] **Step 2: Widen `Api_HttpGet_actions_must_not_have_write_verb_names`**

Rename to `HttpGet_actions_must_not_have_write_verb_names`. Remove line 79 (the same `.Namespace?.Contains(".Api")` filter). Update the comment + message similarly.

- [ ] **Step 3: Run the architecture tests**

Run: `dotnet test --filter "FullyQualifiedName~ArchitectureTests"`
Expected: PASS — proves no remaining controller carries class-level `[AllowAnonymous]` or write-verb GET actions (the deletions in Commit 2 made this true). `Every_controller_action_declares_authorization_intent` (already full-scope) also stays green.

- [ ] **Step 4: Commit** (defer to Task 11)

### Task 10: Drive `pnpm lint` to zero (re-baselined to 39)

**Files:** per the `planning-phase3-spa-migration.md` § 6 dispositions, plus the 5 new items.

- [ ] **Step 1: Capture the current baseline**

Run: `pnpm --dir ProjectCeres.Client lint`
Expected: ~39 problems. Record the exact current breakdown (it may have shifted again since the researcher's 2026-06-29 count: 18 set-state-in-effect, 16 only-export-components, 2 exhaustive-deps, 3 incompatible-library).

- [ ] **Step 2: Apply the 34 planned dispositions**

Follow `planning-phase3-spa-migration.md` § 6 file-by-file: derive-during-render or refactor for the real `set-state-in-effect` cases; per-line disable with `Why:` for the false-positive external-system-sync ones (`use-media-query`, `use-delayed-loading`, `use-api`); split provider files into `provider.tsx` + `context.ts`; per-file disable with `Why:` for shadcn-authored `badge.tsx`/`button.tsx`/`tabs.tsx`; resolve both `exhaustive-deps` (add dep if real stale-closure, else per-line disable + `Why:`).

- [ ] **Step 3: Disposition the 2 new `only-export-components` context files**

`auth/auth-context.tsx`, `theme/theme-context.tsx`: apply the same split-into-`provider.tsx`+`context.ts` treatment used for other providers (preferred), OR per-file disable + `Why:` if the split is disproportionate. Match whichever the § 6 plan used for sibling providers.

- [ ] **Step 4: Investigate + disposition the 3 `incompatible-library` hits**

`LoginTotp.tsx`, `PasswordReset.tsx`, `TotpEnrollStep1ScanVerify.tsx`. Read each flagged hook usage. If it's a genuine concurrent-rendering hazard → fix. If it's a false-positive (the library is used correctly and the rule is over-eager) → per-line disable with a `Why:` naming the library + why it's safe here. Do not bulk-suppress.

- [ ] **Step 5: Drive to zero**

Run: `pnpm --dir ProjectCeres.Client lint`
Expected: 0 errors, 0 warnings. No bare `eslint-disable` (grep: `grep -rn "eslint-disable" ProjectCeres.Client/src | grep -v "Why:"` → no output).

- [ ] **Step 6: Run client tests + build (no regressions from refactors)**

Run: `pnpm --dir ProjectCeres.Client test --run && pnpm --dir ProjectCeres.Client build`
Expected: tests PASS, build within budgets.

- [ ] **Step 7: Commit** (defer to Task 11)

### Task 11: Final grep + doc sweep, tick roadmap, close stage

**Files:**
- Modify: ~10 `docs/` files referencing `/app/`
- Modify: `docs/roadmap-phase-three.md` (Stage 11 checklist + status)

- [ ] **Step 1: Doc-side `/app/` sweep**

Run: `grep -rln "/app/" docs/`
For each hit, update to the new prefix-free URL (or note it as a historical reference if it documents the old behavior). Re-run until only intentional historical mentions remain.

- [ ] **Step 2: Run sync-docs against the full stage diff**

Invoke the `sync-docs` skill against the Stage 11 diff (planning-phase3-spa-migration.md §2/§8, planning-phase3.md §14, api-contract.md, architecture.md as the routing table dictates).

- [ ] **Step 3: Tick the Stage 11 verification checklist**

In `roadmap-phase-three.md`, mark `[x]` every checklist item now covered (URL surface, Program.cs, file deletions, Razor controller stubs, architecture tests, lint, import shelving). Leave genuinely browser-only items `[ ]` and flag them for manual verification. Per Phase E, no `[ ]` may remain under the Stage 11 heading at close-out unless moved to a receiving stage with a back-reference.

- [ ] **Step 4: Full ship-gate verification**

Run (let the Stop hook handle `dotnet test`; run the rest):
```bash
dotnet build ProjectCeres/ProjectCeres.csproj          # 0 errors
pnpm --dir ProjectCeres.Client build                    # budgets clean
pnpm --dir ProjectCeres.Client test --run               # green
pnpm --dir ProjectCeres.Client lint                     # exits 0
```
Plus the frontend checklist: app boots, SPA loads at `/`, every nav page renders, no 404s for legacy assets, IDOR suite green (in the full `dotnet test`).

- [ ] **Step 5: Flip Stage 11 to ✅ Done + commit Commit 3**

```bash
git add -A
git commit -m "feat(11): widen architecture tests, lint to zero, close Stage 11

Widen No_controller_class_has_AllowAnonymous +
HttpGet_actions_must_not_have_write_verb_names to full scope (Razor
controllers gone). Drive pnpm lint to zero (39 -> 0; every former violation
fixed or suppressed-with-Why). Doc /app sweep + sync-docs. Stage 11 Done.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

## Self-Review

**Spec coverage:** Every spec section maps to a task — Commit 1 shelving → Tasks 1–4; SPA host re-homing (revised decision #2) → Task 5; basename + literals → Task 6; 301 + dev middleware → Task 7; MVC teardown → Task 8; arch-test widening → Task 9; lint 39→0 → Task 10; grep/doc/close → Task 11. The ADR-0078 correction → Task 4 Step 1. The test-disposition (client unit tests retained, server integration tests env-aware) → Global Constraints + Task 3 Step 4.

**Placeholder scan:** Two items intentionally require a live `dotnet run` check (Task 5 Step 8 dev-entry question; the exact convention API in Task 3 Step 3) — both are flagged with the exact command to resolve them and a STOP/decision instruction, not left vague. No "TBD"/"add error handling"/"similar to" placeholders.

**Type consistency:** `ShelvedControllerRouteSuppressionConvention` named consistently (Task 3). Host path `dist/app.html` consistent across Tasks 5/7/8 and Global Constraints. `MapFallbackToFile("dist/app.html")` (not `index.html`) consistent. Architecture-test rename targets match the spec verbatim.

## Open items resolved during planning (vs. spec)

- **Spec decision #2 revised** — host is the existing built `dist/app.html` (not a new `wwwroot/index.html`); spec patched 2026-06-29.
- **Test disposition** — client unit tests (mocked) retained as-is; server integration tests stay live via non-beta test environment; new `ShelvedEndpointFencingTests` pins the beta 404.
- **`DecimalModelBinderProvider` removal** — confirmed safe (no API controller binds decimals from form input).
- **One residual to confirm in execution** — `wwwroot/css/site.css` (Razor-layer Tailwind v3 output) may become dead once `Views/` is gone; flagged in Task 10 scope as a lint/cleanup candidate, not assumed.
