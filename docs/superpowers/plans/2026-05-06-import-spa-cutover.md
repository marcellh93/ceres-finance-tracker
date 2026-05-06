# Import Module SPA Cutover (Batch 2 closer)

## Context

The Phase 3 MVC → SPA migration is one feature away from complete: the **Import** row in `docs/planning-phase3-spa-migration.md` §8 is the only one still marked `Pending`. Today `/app/import` resolves to a `PagePlaceholder` and the working flow lives entirely in Razor at `Views/Import/Index.cshtml` + `Summary.cshtml`, driven by `ImportController.cs` and a vanilla-JS step machine inline in the cshtml. `CsvImportProfilesController` (Razor) and its views also still exist for managing saved column-mapping profiles.

All the API endpoints needed to cut over already shipped in Phase 2 and are integration-tested (`ImportApiController`, `ImportHeadersController`, `ImportProfilesApiController` — full CRUD with 90-day soft-delete recover window). This work is therefore a **frontend port + Razor deletion** — no service-layer or schema changes.

The user has explicitly deferred algorithmic upgrades (dual debit/credit columns, confidence-scored transfer detection, transfer-keyword include-list) to follow-up plans. They also chose layout option C: separate routes — `/app/import` for the wizard, `/app/import/profiles` (with nested `/new` and `/:id/edit`) for profile management — matching the Categories/Accounts/Recurring SPA convention rather than the Review-style tabbed-layout.

Intended outcome: `/app/import` and `/app/import/profiles` are real SPA pages built on the existing `/api/import*` endpoints; `Views/Import/*` and `Views/CsvImportProfiles/*` are deleted; both Razor controllers slim to 302 redirects; the SPA-migration doc reflects ✅ Migrated. The page is then a clean foundation to layer the deferred algorithmic work onto.

## Up-front decisions (locked)

- **Layout: option C** — `/app/import` is the wizard; `/app/import/profiles` is the list with nested `/new` and `/:id/edit`. Mirrors `features/categories/CategoriesLayout.tsx` exactly.
- **Wizard navigation: single page with `useState` step.** The wizard owns a `File` object that can't live in a URL, and the SPA has no other multi-step precedent — a reusable `Stepper` primitive is overbuilt for one consumer. A small inline `WizardStepper` header (numbered pills, completed pills clickable for back-nav) gives the visual stepper without abstraction cost. Each step is a pure props-driven subcomponent so tests stay simple.
- **File upload: drag-and-drop + click-to-browse.** Reuse the visual language of `features/movements/AttachmentDropzone.tsx` (drag-over highlight + Upload icon + click fallback) but as a small **single-file** `FileDropzone` primitive — `AttachmentDropzone` is shaped around server-attachment upload + `initialAttachments`, which doesn't match the wizard's "hand a `File` object back to wizard state" need. Place `FileDropzone` in `features/import/` for now; promote to `components/ui/` only if Onboarding (Batch 3) reuses it.
- **Profile resolution stays server-conventional but client-side.** `POST /api/import` does NOT accept `ProfileId`; the SPA fetches the profile via `GET /api/import-profiles/{id}`, copies its mapping into the wizard form state, then POSTs the resolved fields. No new API.
- **`Format` on Profiles Create: user picks (CSV / Excel).** Profiles Create has no file context, and `Format` is immutable on `Update`. Render Format read-only on Edit (locked-control pattern Categories already uses for `categoryTypeId`). The wizard's "Save as profile" CTA infers Format from the uploaded file's extension — no field shown there.
- **"Save these settings as a profile" lives on the result step in v1.** It's the only point the user has both validated mappings and motivation. Inline form, gated on `selectedProfileId === null`. Mirrors Razor `MappingsToSave` exactly.
- **`FlipDebitSign`** is legacy (parsers ignore it). Never surfaced in any SPA UI; always sent as `true`. Removal lives in a future cleanup, not this plan.

## Plan

### Phase 1 — Scaffolding + types

Create the feature folder and TS surface; restructure routing.

Files to create:
- `ProjectCeres.Client/src/app/features/import/import-api.ts` — URLs + types: `ImportFormat ('Csv' | 'Excel')`, `ImportColumnMappings`, `HeaderDetectionResult`, `ImportResult`, `ImportProfileListItemDto`, `CreateImportProfileRequest`, `UpdateImportProfileRequest`. Field shapes match the C# DTOs read in `ImportProfilesApiController.cs` and `CsvColumnMappings.cs`.

Files to modify:
- `ProjectCeres.Client/src/app/pages/Import.tsx` — re-export from `features/import/ImportWizard.tsx` (mirrors `pages/Settings.tsx` shape).
- `ProjectCeres.Client/src/app/App.tsx` — replace the single `/import` route with:
  ```
  <Route path="import" element={<ImportWizard />} />
  <Route path="import/profiles" element={<ProfilesLayout />}>
    <Route path="new" element={<ProfileCreate />} />
    <Route path=":id/edit" element={<ProfileEdit />} />
  </Route>
  ```

Verification: `pnpm build` typechecks; routes resolve to placeholders; no regressions in `pnpm test`.

### Phase 2 — Profiles SPA (list + create + edit + archive/reactivate)

Mirror the Categories template line-for-line; the soft-delete UX matches per the memory rule "Archive always ships with Reactivate".

Files to create under `features/import/`:
- `ProfilesLayout.tsx` — list, "Include archived" `Switch` (URL-synced via `useSearchParams`), debounced search, `useApi<ImportProfileListItemDto[]>(buildListUrl(includeInactive))`, `<Outlet context={{ refetch }}/>` when `useMatch('/import/profiles/new')` or `:id/edit` matches. Mirrors `features/categories/CategoriesLayout.tsx` lines 31–60.
- `ProfilesTable.tsx` — Name / Format / Created / status. Row menu: Edit, Archive (AlertDialog), Reactivate (only for archived rows). Archived rows show `daysUntilPurge` chip — surface the 90-day window the API already enforces.
- `ProfileForm.tsx` — shared Create/Edit form: `name`, `format` (Select, locked on Edit), `sheetName` (only when format=Excel), four mapping fields (Date / Amount / Description / Category), `flipDebitSign` always implicit `true` (no UI). Submit returns `{ ok: boolean }` like `SettingsForm`.
- `ProfileCreate.tsx` + `ProfileEdit.tsx` — page wrappers identical to `CategoryCreate`/`CategoryEdit`: load, render `ProfileForm`, on submit POST/PATCH, toast, `ctx.refetch()`, navigate `/import/profiles`.
- Vitest for each: `ProfilesLayout.test.tsx` (renders rows + archive switch toggles URL), `ProfilesTable.test.tsx` (row menu actions), `ProfileForm.test.tsx` (Format-locked-on-edit, sheet conditional).

Verification: in the browser at `/app/import/profiles` create a profile → appears in list; edit → Format disabled, save persists; archive → row hidden until "Include archived" on; reactivate → row returns to active; days-until-purge visible on archived rows. Run `web-design-guidelines` against the four new files.

### Phase 3 — Wizard skeleton + step 1 + step 2

Files to create under `features/import/`:
- `ImportWizard.tsx` — owns `step: 1|2|3|4`, `file`, `accountId`, `selectedProfileId`, `mappings`, `headers`, `result`. Renders `<WizardStepper currentStep={step} onJumpBack={...}/>` then the active step component. Each step receives only the state slice it needs.
- `WizardStepper.tsx` — numbered pills (1, 2, 3, 4 as Submit/Result), completed pills clickable for back navigation. Card-less wrapper using `Tabs`-style tokens from the design system.
- `FileDropzone.tsx` — single-file drop target. Drag-over highlight, click-to-browse fallback via hidden `<input type="file">`, accept filter (`.csv,.xlsx`), 10 MB guard with toast pre-handoff, keyboard activation (Enter/Space), `aria-label`. Props: `accept`, `maxBytes`, `onFile(file)`, `selectedFile?`. Mirrors `AttachmentDropzone`'s drag-over visual treatment.
- `FileDropzone.test.tsx` — drag-drop event, click-to-browse, oversized-file rejection, accept-filter rejection, keyboard activation.
- `StepFile.tsx` — `FileDropzone` + `AccountCombobox` (reuse existing component from Movements quick-add) + Continue button. Selected file shows name, size, and a "Replace" affordance (re-opens the picker). On Continue: call `POST /api/import/headers` (file as FormData), store `HeaderDetectionResult` in wizard state, advance to step 2. If header detection fails, advance anyway with a warning toast — user can map manually (matches Razor degrade-gracefully behavior in `Index.cshtml` line 200).
- `StepMapping.tsx` — saved-profile picker (`useApi<ImportProfileListItemDto[]>('/api/import-profiles')`), four column `Select`s populated from `headers`, pre-filled by `HeaderDetectionResult` (or by selected profile's mapping). Validation: Date / Amount / Description must be set before Continue (Category optional).
- `StepFile.test.tsx`, `StepMapping.test.tsx`, `WizardStepper.test.tsx`.

Verification: at `/app/import` upload a real BBVA/Sabadell-style CSV → headers fetched, three columns auto-selected; toggle profile picker → mapping fields populate from profile; large file (>10 MB) → blocked with toast pre-network. Test 375 px layout on every step.

### Phase 4 — Wizard step 3 + result + save-as-profile

Files to create:
- `StepReview.tsx` — read-only summary list of file / account / mappings (mirrors Razor `populateReview()` from `Index.cshtml`). Buttons: Back, Import.
- `StepResult.tsx` — five summary cards (`RowsImported`, `RowsReconciled`, `RowsFlagged`, `RowsStaged`, `RowsFailed`) styled with `summary-card--success/info/warning/danger/neutral` tokens already in `docs/design-system.md`. Deep-links: Reconciled → `/app/review?tab=reconciliations`; Staged → `/app/review?tab=transfers`; Flagged → `/app/movements?needsReview=true`. Errors block at the bottom if `errors.length > 0`. Buttons: "Import another file", "View transactions".
- `SaveProfilePrompt.tsx` — inline card on `StepResult` shown only when `selectedProfileId === null`. Single text input for name + Save / Skip. Format inferred from `file.name.endsWith('.xlsx') ? 'Excel' : 'Csv'`. Submits `CreateImportProfileRequest`; on success replaces itself with a "Saved as <name>" confirmation row.
- Submit handler in `ImportWizard.tsx`: builds multipart body (file + accountId + mapping fields, all flat), `POST /api/import`, stores `ImportResult`, advances to step 4. On 4xx surface message via toast, stay on step 3.
- `StepReview.test.tsx`, `StepResult.test.tsx`, `SaveProfilePrompt.test.tsx`, `ImportWizard.test.tsx` (full happy-path with mocked fetch).

Verification: end-to-end in the browser — upload CSV with two existing-account matches and one transfer pair → result shows correct counts; "Reconciled: 2" deep-links to Review reconciliations tab; "Staged: 1" deep-links to Review transfers tab; save-profile prompt appears, name it, refresh `/app/import/profiles` → profile present.

### Phase 5 — Razor cutover + doc sync (single commit)

This phase is one commit per the memory rule "Sync docs before SPA-migration commits".

Files to modify:
- `ProjectCeres/Controllers/ImportController.cs` — slim to two `Redirect()` actions (302 default):
  - `GET /Import` → `/app/import`
  - `GET /Import/Summary` → `/app/import`
  - Delete `[HttpPost] Index`, `[HttpPost] SaveProfile`, `PopulateViewBagAsync`.
- `ProjectCeres/Controllers/CsvImportProfilesController.cs` — slim similarly:
  - `GET /CsvImportProfiles` → `/app/import/profiles`
  - `GET /CsvImportProfiles/Create` → `/app/import/profiles/new`
  - `GET /CsvImportProfiles/Edit/{id}` → `/app/import/profiles/{id}/edit`
  - `GET /CsvImportProfiles/Delete/{id}` → `/app/import/profiles`
  - Delete all `[HttpPost]` actions.

Files to delete:
- `ProjectCeres/Views/Import/Index.cshtml`
- `ProjectCeres/Views/Import/Summary.cshtml`
- `ProjectCeres/Views/CsvImportProfiles/*.cshtml` (audit before deletion)
- `ProjectCeres/ViewModels/ImportUploadViewModel.cs` (Razor-only — `ImportRequestViewModel` keeps its current API role)
- `ProjectCeres/ViewModels/ImportSummaryViewModel.cs` (Razor-only; `ImportResult` is the API DTO and stays)
- Any Razor-only ViewModels in `ProjectCeres/ViewModels/` keyed off `CsvImportProfile*` after grep.

**Keep**: `ImportRequestViewModel`, `ImportColumnMappings` (in `CsvColumnMappings.cs`), `HeaderDetectionResult`, `ImportResult`, `ImportProfileListItemDto`, `Create/UpdateImportProfileRequest`, `ParsedImportRow`, all services and tests.

Doc sync (same commit):
- `docs/planning-phase3-spa-migration.md` §2 — flip `ImportController` and `CsvImportProfilesController` rows to "✅ Migrated 2026-05-06" with one-line summaries pointing at this plan.
- `docs/planning-phase3-spa-migration.md` §8 row 7 — flip `Import | Pending` to `Import | ✅ Migrated 2026-05-06 | <one-line summary, plan path>`.
- `docs/planning-phase3.md` §14 — if it lists the SPA-migration row count, bump.
- Run `sync-docs` skill against the diff before committing.

Verification (full UX/UI checklist per `docs/design-system.md`):
- `dotnet test` green; `pnpm test` green; `pnpm build` green.
- Manual: navigate to legacy `/Import`, `/Import/Summary`, `/CsvImportProfiles`, `/CsvImportProfiles/Create`, `/CsvImportProfiles/Edit/<id>` — each 302s to the right SPA URL.
- Golden path: upload real CSV → mapping → review → import → result with counts.
- Empty state: no profiles list, no archived rows.
- Error state: upload non-CSV/XLSX, oversized file, broken CSV row.
- 375 px: every page renders without horizontal scroll.
- Keyboard: tab through wizard, tab through Profiles row menu.
- Run `web-design-guidelines` skill on `features/import/*`.

## Critical files

- `<repo>/ProjectCeres.Client/src/app/App.tsx` — route restructure (Phase 1, Phase 5 doesn't touch this).
- `<repo>/ProjectCeres.Client/src/app/pages/Import.tsx` — re-export update (Phase 1).
- `<repo>/ProjectCeres.Client/src/app/features/categories/CategoriesLayout.tsx` — template to mirror for `ProfilesLayout` (Phase 2).
- `<repo>/ProjectCeres.Client/src/app/features/settings/SettingsPage.tsx` + `SettingsForm.tsx` — shell + form-submit pattern to mirror (Phase 2, 3, 4).
- `<repo>/ProjectCeres.Client/src/app/lib/use-api.ts` — reused as-is for all GETs.
- `<repo>/ProjectCeres/Controllers/Api/ImportApiController.cs` — submit contract (no change).
- `<repo>/ProjectCeres/Controllers/Api/ImportHeadersController.cs` — header-detection contract (no change).
- `<repo>/ProjectCeres/Controllers/Api/ImportProfilesApiController.cs` — profiles CRUD contract (no change).
- `<repo>/ProjectCeres/Controllers/ImportController.cs` — slim to redirects (Phase 5).
- `<repo>/ProjectCeres/Controllers/CsvImportProfilesController.cs` — slim to redirects (Phase 5).
- `<repo>/docs/planning-phase3-spa-migration.md` — §2 + §8 row 7 sync (Phase 5).

## Risks & edge cases

**v1 must handle**:
- File >10 MB → client-side guard with toast before upload.
- Header detection failure → degrade to manual mapping with toast warning (matches Razor today).
- Profile selected then file changed → re-validate that profile's columns exist in new file's headers; warn inline if any missing.
- Excel sheet missing on selected profile → server returns 400; surface message inline on review step.
- Archived profile recovery within 90-day window — `daysUntilPurge` already on the DTO, surface as chip.
- Focus management on step transitions (`tabIndex={-1}` on wizard heading like `ReviewLayout`).
- Concurrent submit click → disable Import button while POST inflight.
- 375 px wrap on summary cards (5 cards → 2-up grid).

**Deferred (out of scope)**:
- Empty-column detection (needs row preview).
- Progress indicator for slow imports (delayed Sonner toast at ~3s good enough for v1).
- `FlipDebitSign` UI — legacy field, parser-ignored, never shown.
- Dual debit/credit columns, confidence scoring, transfer keyword settings — separate plans on top of this cutover.
