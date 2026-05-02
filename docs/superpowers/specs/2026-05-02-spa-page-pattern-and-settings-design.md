# SPA Page Pattern (Settings as the template)

> **Status:** Approved 2026-05-02. Settings is the first SPA page in Phase 3 Batch 2 of the frontend migration. The patterns established here are the template for the next six pages: Categories, Accounts, Recurring, Reports, Review, Import.
>
> **Scope:** Build a real Settings page replacing the `<PagePlaceholder>`, plus delete the Razor `Settings/Edit` view and POST action in the same commit. The Razor `SettingsController` becomes a 302 redirect to `/app/settings`.

## Goal

Lock the SPA-page implementation pattern (file structure, data flow, error handling, loading/success states, testing, Razor cutover) on this small page before applying it to the bigger ones. Every decision below is made with that template-setting purpose in mind.

## Locked decisions (one-liners)

| Question | Decision |
|---|---|
| What happens to the rest of the app after Settings PATCH succeeds? | The `lib/use-settings.ts` cache invalidates and notifies subscribers — no page reload. |
| Auto-save vs explicit Save button? | Explicit Save button, disabled when nothing changed. Matches every other form in the SPA. |
| Where does a save failure surface? | Single red toast: `"Couldn't save. Try again."` Form keeps the user's edits, Save stays enabled to retry. |
| How are tests structured? | `vi.stubGlobal('fetch', ...)` per test, no MSW. Matches existing Movements/Budgets test convention. |
| Folder shape? | `features/settings/` with separate Page (data) and Form (UI) components. Mirrors Budgets/Movements. |

## File structure

```
ProjectCeres.Client/src/app/features/settings/
  settings-api.ts                 # URL constants + DTO types — no logic
  SettingsPage.tsx                # Page glue: GET, PATCH, toast, cache refetch
  SettingsPage.test.tsx
  SettingsForm.tsx                # Pure UI — form state, dirty detection, four fields
  SettingsForm.test.tsx
ProjectCeres.Client/src/app/pages/Settings.tsx
                                  # One-line re-export of SettingsPage
ProjectCeres.Client/src/app/lib/use-settings.ts
                                  # Adds public refetchSettings() export
```

**Three units, three responsibilities:**

- **`settings-api.ts`** — URL strings + DTO types. Mirrors `budgets-api.ts`. Contains:
  - `SETTINGS_URL = '/api/settings'`
  - `CURRENCIES_URL = '/api/currencies'`
  - `type SettingsDto = { numberFormat, dateFormat, defaultCurrencyCode, defaultCurrencySymbol, periodStartDay }`
  - `type CurrencyOptionDto = { id: number, code: string, symbol: string, name: string }`
  - `type UpdateSettingsRequest = { numberFormat, dateFormat, defaultCurrencyId, periodStartDay }`

- **`SettingsForm.tsx`** — pure UI. Owns local form state, computes `isDirty` by shallow comparison against `initialValues`, renders four fields. Knows nothing about fetch or toast. Props:
  - `initialValues: SettingsFormValues`
  - `currencies: CurrencyOptionDto[]`
  - `onSubmit: (values) => Promise<{ ok: true } | { ok: false }>`

- **`SettingsPage.tsx`** — page glue. Calls `useApi<SettingsDto>(SETTINGS_URL)` and `useApi<CurrencyOptionDto[]>(CURRENCIES_URL)`. Renders Skeleton / error / Form. The `onSubmit` it passes does the PATCH, on success calls `refetchSettings()` and `toast.success('Saved.')`, on failure `toast.error("Couldn't save. Try again.")`.

**Pattern locked for the next six pages:**
> `features/<area>/` holds the feature. `<area>-api.ts` for URLs + DTOs. `<Area>Page.tsx` owns data and side-effects. Pure UI components are siblings. The route file in `pages/` is a one-liner re-export. Tests are co-located.

## Data flow

**On mount (SettingsPage):**

1. Call `useApi<SettingsDto>(SETTINGS_URL)` and `useApi<CurrencyOptionDto[]>(CURRENCIES_URL)` in parallel.
2. While either is loading, render `<Skeleton>` matching the form's rough height.
3. If either errors, render an inline error block (`"Couldn't load settings."`) with a Retry button that calls both `refetch()` functions.
4. Once both succeed, render `<SettingsForm initialValues={settings} currencies={currencies} onSubmit={handleSubmit} />`.

**On user input (inside SettingsForm):**

1. User changes a field. Local state updates.
2. Form recomputes `isDirty = !shallowEqual(values, initialValues)`. Save button enabled only when `isDirty`.
3. User clicks Save → `submitting = true`, button disabled and shows `"Saving…"`, `props.onSubmit(values)` called.

**On submit (SettingsPage's handleSubmit):**

1. PATCH `/api/settings` with `Content-Type: application/json`, body `JSON.stringify(values)`.
2. **2xx response:** call `refetchSettings()` (new export), `toast.success('Saved.')`, return `{ ok: true }`.
3. **Non-2xx (any error code, including 422 / network error):** `toast.error("Couldn't save. Try again.")`, return `{ ok: false }`.

**On submit response (back in SettingsForm):**

- `ok: true` → reset `initialValues` to the saved values, `isDirty` becomes false, Save greys out, `submitting = false`.
- `ok: false` → `submitting = false`. Inputs retain user edits. Save stays enabled. User can retry.

## Public `refetchSettings()` in `lib/use-settings.ts`

The existing module-level cache has `subscribers` and `notify()` infrastructure. Add one new export:

```typescript
/**
 * Force a refresh of the cached settings. Notifies every useSettings()
 * subscriber when the new data arrives. Call this after PATCH /api/settings
 * succeeds so the rest of the app picks up format/currency changes
 * without a page reload.
 */
export function refetchSettings(): Promise<void> {
  cache.promise = null;     // clear the dedup so startFetch actually runs
  cache.loading = false;
  return startFetch();
}
```

**Specifics:**

- `cache.promise = null` because `startFetch` short-circuits if it's set.
- `cache.loading = false` keeps state consistent before `startFetch` flips it back to `true`.
- Returns the promise so callers can chain if needed; we won't await today.
- Does NOT clear `cache.data` first — old data stays visible for the ~50–100 ms the GET takes, avoiding a flash of empty state in every other component.

Only `SettingsPage` calls `refetchSettings()`. Every other consumer keeps using `useSettings()` unchanged.

## Loading, error, success states

| State | Where | Behaviour |
|---|---|---|
| Initial GET in flight | Page | `<Skeleton>` matching the form's height |
| Submitting (PATCH in flight) | Form | Save button disabled, label `"Saving…"`. Inputs stay enabled. |
| GET fails | Page | Inline error: `"Couldn't load settings."` + Retry button calling both `refetch()`s |
| PATCH fails | Form (via toast) | `toast.error("Couldn't save. Try again.")`, Save re-enabled, edits retained |
| PATCH succeeds | Form (via toast) | `toast.success('Saved.')`, `refetchSettings()` invalidates the cache, Save greys out |

**Two specifics worth calling out:**

1. **Number-format flip.** When the user changes `numberFormat` and saves, every amount-formatter elsewhere in the app picks up the new format on the next render via the cache refetch. No page reload, no flash.
2. **Period-start-day flip.** Components that consume `useSettings()` re-render with new currency code/symbol after the cache refetch. Dashboard cards (e.g. `MtdCard`) consume `useSettings()` for currency display and update accordingly, but the *numbers* shown come from `/api/dashboard/summary` and are not refetched here. The user sees correct symbols immediately; correct period-aligned totals appear next time the dashboard is fetched (a navigation, a manual refresh, or a separate dashboard refetch — all out of scope for this spec). That is correct behaviour and not worth special-casing.

## Testing strategy

Three test files, three responsibilities. ~15 tests total.

### `SettingsForm.test.tsx` (UI in isolation, no fetch)

- Renders all four fields with initial values.
- Save button disabled when nothing changed.
- Save button enables when a field changes.
- Save button shows `"Saving…"` while `onSubmit` is pending.
- Save button greys back out after `onSubmit` returns `{ ok: true }`.
- Inputs retain user edits when `onSubmit` returns `{ ok: false }`.
- Period-start-day rejects values outside 1–31.

`onSubmit` is mocked with `vi.fn()`. No fetch mocking.

### `SettingsPage.test.tsx` (data + plumbing)

- Renders skeleton while loading.
- Renders the form when data arrives.
- Renders error + Retry when GET fails.
- Retry re-fetches after error and renders the form.
- PATCH success: `toast.success('Saved.')` called and `refetchSettings()` called.
- PATCH failure: `toast.error` called, Save button still enabled.

Mocks: `vi.stubGlobal('fetch', mockFn)` keyed by URL, `vi.mock('sonner', ...)`, `vi.mock('@/app/lib/use-settings', ...)` to spy on `refetchSettings`.

### `use-settings.test.ts` (extends the existing file)

- `refetchSettings()` triggers a new fetch and notifies subscribers.
- `refetchSettings()` updates `cache.data` to the new response.

## Razor cutover (same commit)

**Delete:**
- `ProjectCeres/Views/Settings/Edit.cshtml`
- `ProjectCeres/ViewModels/SettingsEditViewModel.cs` (only Razor uses it)
- `[HttpPost] Edit(SettingsEditViewModel vm)` action and `PopulateViewBagAsync` helper in `SettingsController.cs`
- `SettingsService.UpdateAsync(SettingsEditViewModel)` and the matching method on `ISettingsService` — both become unreferenced once the POST is gone
- Test methods in `SettingsServiceTests.cs` that call `UpdateAsync(...)`. Coverage for the same behaviour exists at the API level (`Patch_*` tests in `SettingsApiTests.cs` against `TryUpdateAsync`). Test methods that call `GetAsync()` or `EnsureExistsAsync()` **stay** — those service methods are still in use by both the API and startup.

**Keep:**
- `SettingsController.cs` slimmed to a redirect-only controller:
  ```csharp
  public class SettingsController : Controller
  {
      public IActionResult Edit() => Redirect("/app/settings");
  }
  ```
- 302 (the default), not 301. Per migration doc §8: "Do not use 301 (`RedirectPermanent`) for these migration redirects — 301s cache aggressively in browsers." The redirect itself disappears in the final cleanup batch.
- `ISettingsService.TryUpdateAsync(UpdateSettingsRequest)` — the API path. Untouched.
- `ISettingsService.GetAsync()` — used by both API and other services. Untouched.
- `ISettingsService.EnsureExistsAsync()` — startup hook. Untouched.

**Verification gate before commit:**

1. `dotnet build` — must succeed (catches dangling references to deleted ViewModel and `UpdateAsync`).
2. `dotnet test` — must pass.
3. `pnpm test` in `ProjectCeres.Client` — must pass; new tests in scope here.
4. **Manual click-through:** open `/Settings/Edit` in a browser, verify 302 to `/app/settings`, verify form renders + Save round-trips successfully.

## Out of scope

- Introducing TanStack Query or any other data-fetching library.
- Restructuring the existing `lib/use-settings.ts` cache module beyond adding the one new export.
- Building a generic `useMutation` helper. Settings has one PATCH; if a pattern emerges across the next pages, extract it then, not now.
- Visual redesign of the Settings UX. The form fields and labels match what the Razor page exposes today; visual polish is a separate concern.

## Follow-ups (deliberate non-goals for this commit)

- The "background processes and non-HTTP contexts" hazard documented in `docs/multi-tenancy-strategy.md` is unaffected by this work; it'll be addressed in the auth batch.
- The next six SPA pages (Categories, Accounts, Recurring, Reports, Review, Import) follow this exact template. If any page reveals a gap in the pattern, update this spec, not the per-page docs.
