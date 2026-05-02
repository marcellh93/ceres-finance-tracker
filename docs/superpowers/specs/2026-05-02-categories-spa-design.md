# Categories SPA page

> **Status:** Approved 2026-05-02. Categories is the second SPA page in Phase 3 Batch 2 of the frontend migration. The Settings page commit (e842250) locked the SPA-page template; this spec applies that template and adds the page-specific design choices.
>
> **Scope:** Build a real Categories page replacing the `<PagePlaceholder>`, with nested routes for create/edit, plus delete the Razor `Categories/*` views and POST actions in the same commit. The Razor `CategoriesController` becomes a 302 redirect to `/app/categories`.

## Goal

Optimise the Categories list for the user's actual task weighting:

1. **Find a specific category by name and edit or archive it** — most common.
2. **Create a new category** — occasional.
3. **Browse the full list for audit** — rare.

The layout must stay friendly on mobile without degenerating to "scroll past one whole group to reach the other."

## Locked decisions

| Question | Decision |
|---|---|
| Type grouping shape | **Tabs** (`Income` / `Expense`). One type visible at a time. No stacked tables, no side-by-side columns. |
| Default tab | `Expense` (the bigger group; most edit traffic). |
| Search | Persistent text input above the active tab. Debounced 200 ms via the existing `useDebounced` hook, filters client-side. |
| Show inactive | Hidden by default. `<Switch>` toggle "Include archived" sits next to the search input. URL param `?includeInactive=true`. |
| System category visibility | Always shown in their type's tab, **with a tooltip-bearing badge** and no row actions. Sort to the bottom of their tab. |
| System badge tooltip wording | Exactly `"Created by the system. These are required for imports and accounting and cannot be edited or archived."` |
| Create/Edit affordance | Nested routes `/app/categories/new` and `/app/categories/:id/edit`. Same `CategoryForm` for both. Matches `BudgetsLayout` precedent. |
| Archive UX | Row `⋯` menu → "Archive…" → `<ConfirmDialog>` → `PATCH /api/categories/:id/archive`. Errors surface as `toast.error` with the server's message. |
| Form fields | Name (required), CategoryType (required, **fixed on Edit** — server-immutable on the wire too), LifestyleTag (optional: None / Needs / Wants / Savings). |
| LifestyleTag picker | Popover+Command (matches the locked Combobox idiom). "None" is a first-class menu item. |
| CategoryType picker | Popover+Command on Create. Read-only display on Edit (server's `UpdateCategoryRequest` doesn't even bind the field — the disabled UI is a hint, not a security boundary). |
| Test mocking | `global.fetch = mockFetch as unknown as typeof fetch`, `vi.mock('sonner', …)`. No MSW. |

## File structure

```
ProjectCeres.Client/src/app/features/categories/
  categories-api.ts            # URL constants + DTO types — no logic
  CategoriesLayout.tsx         # Tabs + filter bar + outlet for new/edit
  CategoriesLayout.test.tsx
  CategoriesTable.tsx          # Table for one tab's rows; pure UI
  CategoriesTable.test.tsx
  CategoryRowMenu.tsx          # ⋯ menu (Edit, Archive…) — pure UI
  CategoryRowMenu.test.tsx
  CategoryForm.tsx             # Create + Edit form; pure UI
  CategoryForm.test.tsx
  CategoryCreate.tsx           # Page glue for /new — POST + toast
  CategoryCreate.test.tsx
  CategoryEdit.tsx             # Page glue for /:id/edit — GET + PATCH + toast
  CategoryEdit.test.tsx
ProjectCeres.Client/src/app/pages/Categories.tsx
                               # One-line re-export of CategoriesLayout
```

App routing changes (in `src/app/App.tsx`):

```tsx
<Route path="categories" element={<CategoriesLayout />}>
  <Route path="new" element={<CategoryCreate />} />
  <Route path=":id/edit" element={<CategoryEdit />} />
</Route>
```

The layout swaps content based on `useMatch('/app/categories/new')` / `useMatch('/app/categories/:id/edit')`, mirroring `BudgetsLayout.tsx`.

**Pattern continuation:** matches the Settings template (Page+Form split) but expanded for a list+CRUD shape, mirroring the existing Budgets folder.

## Layout grammar

Top-of-page, in order:

1. **Header.** `<header>` with `<h1>Categories</h1>` plus muted description: `"Manage how transactions are classified. System categories used by imports and accounting cannot be edited."`
2. **Filter bar.** Sticky-ish (verify in implementation) row containing:
   - Tabs: `Income` / `Expense`. Default `Expense`. URL param `?type=income|expense`.
   - Search input: `<Input>` with placeholder `"Filter categories…"`. Updates `?q=` after 200 ms debounce.
   - "Include archived" `<Switch>` with label.
   - "New category" `<Button>` (right-aligned at desktop, full-width at mobile). Links to `/app/categories/new`.
3. **Table.** One `<Card>` containing the active tab's rows. No internal section dividers — the type *is* the tab.

Mobile breakpoint: filter bar wraps. Search input takes its own row, tabs and Create button share the next row, switch sits below. Table renders identically — same row anatomy, no truncation, no horizontal scroll.

### Row anatomy

```
| Name (text-sm)                    [Lifestyle?] [System ⓘ?] [Archived?] ⋯ |
```

- **Name.** `text-sm` Inter regular. Full row is the tap target for keyboard nav; visible affordance is the `⋯`.
- **Lifestyle badge.** `<Badge variant="secondary">` with the tag value. Only renders if `lifestyleTag` is non-null. Expense rows only.
- **System badge.** `<Badge variant="outline">` containing the tag text "System" + a small `Info` icon as a `<Tooltip>` trigger. Only on rows where `isSystem === true` *or* the row id is one of the reserved Uncategorized GUIDs. Sorted to the bottom of the tab.
- **Archived badge.** `<Badge variant="secondary">"Archived"</Badge>`. Only when `?includeInactive=true` AND `isActive === false`. Row gets `opacity-60`.
- **⋯ menu** (`<DropdownMenu>`). Items: "Edit" (navigates), "Archive…" (opens ConfirmDialog). Hidden entirely for system rows. For archived non-system rows, only "Edit" remains.

### Sort order

Within the active tab:
1. Active user categories, A→Z.
2. Archived user categories, A→Z (only when toggle on).
3. System rows, A→Z (always last in their tab).

## Data flow

**On mount (CategoriesLayout):**

1. Read `?type=`, `?q=`, `?includeInactive=` from URL.
2. Fire `useApi<CategoryListItemDto[]>` against `/api/categories?includeInactive=<bool>` (no `typeId` in the request — type filtering is client-side because the dataset is small and we already have the tabs derived from the same data).
3. While loading, render skeleton rows inside the Card.
4. On error, render `<CardError section="Categories" onRetry={…} />`.
5. On success, partition by `categoryTypeName` and feed the active tab into `<CategoriesTable>`.

**Search filtering (CategoriesLayout, client-side):**
1. `useDebounced(searchInput, 200)` produces the query string applied to the URL.
2. The visible rows are computed by `rows.filter(r => r.name.toLowerCase().includes(q.toLowerCase()))`.
3. When the search box matches no rows in the active tab, the table renders the empty state instead of the table body.

**Create flow (CategoryCreate):**
1. Render `<CategoryForm mode="create" />`.
2. On submit: POST `/api/categories` → on 2xx `toast.success("Created.")`, navigate back to `/app/categories?type=<typeName>` (return to the list with the new category's type tab active), parent layout refetches.
3. On non-2xx: `toast.error("Couldn't save. Try again.")`, form keeps user edits.

**Edit flow (CategoryEdit):**
1. Read `:id` from URL params.
2. `useApi<CategoryDetailDto>(\`/api/categories/${id}\`)`.
3. While loading, render the form skeleton. On 404, render an "Couldn't find that category" banner with a link back to `/app/categories`.
4. Render `<CategoryForm mode="edit" initialValues={…} />`.
5. On submit: PATCH `/api/categories/:id` → `toast.success("Saved.")`, navigate back, parent refetches.
6. On 422 with code `SYSTEM_CATEGORY_IMMUTABLE`: defensive — should be unreachable since system rows have no Edit button. Toast the server message.

**Archive flow (within CategoryRowMenu):**
1. User clicks `⋯ → Archive…`.
2. `<ConfirmDialog>` opens with title `"Archive '<name>'?"` and body `"You can still see archived categories with the toggle. This won't affect existing transactions."` Buttons: "Archive" (destructive variant), "Cancel".
3. On confirm: PATCH `/api/categories/:id/archive` → `toast.success("Archived.")`, layout refetches.
4. On 409 `CATEGORY_IN_USE`: `toast.error("This category has transactions. Reassign them before archiving.")`.
5. On any other non-2xx: `toast.error("Couldn't archive. Try again.")`.

## Form design

`CategoryForm` is shared between Create and Edit. Props:

```ts
type Props = {
  mode: 'create' | 'edit';
  initialValues: CategoryFormValues;
  categoryTypes: CategoryTypeDto[];   // for the Create-mode picker
  onSubmit: (values) => Promise<{ ok: true } | { ok: false }>;
};
```

Fields:

- **Name.** `<Input>` with `aria-label`. Required. Trimmed before send.
- **CategoryType.** Popover+Command on Create (Income / Expense). Read-only label on Edit, with the value displayed as text plus the Info-tooltip text `"The type cannot be changed after creation. Create a new category if you need a different type."`. The form does not even *send* `categoryTypeId` in the PATCH body — the request DTO `UpdateCategoryRequest` only has Name and LifestyleTag, so the server cannot accept a change.
- **LifestyleTag.** Popover+Command with options `[None, Needs, Wants, Savings]`. "None" maps to `null` in the DTO. Optional.

Buttons:

- **Save** (primary) — disabled when form is not dirty.
- **Reset** (ghost) — disabled when form is not dirty. Restores `initialValues`.

No "Cancel" button. Navigating back is via the URL bar / back button. (Settings precedent — Settings has no Cancel, only Reset.)

## Loading, error, success states

| State | Where | Behaviour |
|---|---|---|
| Initial GET in flight | Layout | Filter bar reserves height; Card renders 4–5 muted skeleton rows. |
| Search debounce in flight | Layout | No spinner — filtering is client-side, change is ≤200 ms. |
| Submitting (POST/PATCH in flight) | Form | Save button shows `"Saving…"`, disabled. Inputs stay enabled. |
| GET fails | Layout | `<CardError section="Categories" onRetry={refetch} />`. |
| Edit GET 404 | CategoryEdit | Banner: `"That category doesn't exist."` plus a link back to `/app/categories`. |
| POST/PATCH success | Form (via toast) | `toast.success("Created.")` or `toast.success("Saved.")`, navigate back, layout refetches. |
| POST/PATCH 422 | Form (via toast) | `toast.error("Couldn't save. Try again.")`. Form retains values, Save re-enables. |
| Archive 409 | RowMenu (via toast) | `toast.error("This category has transactions. Reassign them before archiving.")`. |

### Empty states

| State | Treatment |
|---|---|
| Active tab has rows but search matches nothing | Replace table body with `<PanelEmpty>`-style block: italic muted `"No categories match '<query>'."` plus a "Clear search" link button that clears only `?q=`, leaving the `?type=` tab and the `?includeInactive=` toggle as the user set them. |
| Archived toggle on, no archived rows in this tab | Below the active rows: italic muted hint `"No archived categories in this tab."` |
| No categories at all (impossible — seed data) | `<PanelEmpty>` with `Tags` icon (verify Lucide export at implementation), heading `"No categories yet"`, primary `<Button>` linking to `/app/categories/new`. |

## Devtools / "what stops a user from changing CategoryType on Edit?"

Frontend `disabled` attributes are UX hints, not security boundaries. The actual guarantee is structural at the API: `UpdateCategoryRequest` has no `CategoryTypeId` field, and `CategoryService.TryUpdateAsync` only assigns Name and LifestyleTag. Even if a user enables the disabled control in devtools and crafts a malicious PATCH, the server cannot accept a CategoryType change because it isn't in the request DTO. Verified in source on 2026-05-02.

This is the pattern: never use `disabled`/`readonly`/`hidden` on the frontend to enforce a rule. Either omit the field from the request DTO (here) or reject changes server-side. The frontend is for communicating the rule visually; the server is for enforcing it.

## Testing

Three test layers, ~25 tests total.

### Layout / list (`CategoriesLayout.test.tsx`)

- Renders skeleton while loading.
- Renders the Expense tab by default.
- Switching tab updates `?type=` and changes visible rows.
- Search input updates `?q=` after debounce; rows filter accordingly.
- Search empty state when no rows match.
- "Include archived" toggle adds archived rows; default off.
- Archived rows render with the Archived badge and opacity treatment.
- System rows render with the System badge + tooltip; no `⋯` menu visible.
- System rows sort to the bottom of their tab.
- GET error renders `CardError` with Retry.

### Row menu (`CategoryRowMenu.test.tsx`)

- Renders Edit and Archive items for active user rows.
- Renders only Edit for archived non-system rows.
- Renders nothing (or no trigger) for system rows.
- Clicking Edit calls the navigate prop with `/app/categories/:id/edit`.
- Clicking Archive opens the ConfirmDialog with the right name.

### Form (`CategoryForm.test.tsx`)

- Renders all three fields with initial values.
- CategoryType is editable on Create, read-only on Edit.
- LifestyleTag picker shows None / Needs / Wants / Savings.
- Save and Reset are disabled when not dirty; enable when dirty; both disable after a successful save.
- Reset restores initialValues.
- Save shows "Saving…" while in flight.

### Page glue (`CategoryCreate.test.tsx`, `CategoryEdit.test.tsx`)

- Create POST success → toast.success("Created.") + navigates back.
- Create POST 422 → toast.error, form retains values.
- Edit GET 404 → renders "That category doesn't exist." banner.
- Edit PATCH 409 (defensive — for archive flow re-entered through the form path; in practice unreachable) → toast.error.

Mocks: `vi.stubGlobal('fetch', …)` keyed by URL, `vi.mock('sonner', …)`, `vi.mock('react-router-dom', …)` for the navigate spy where needed.

## Razor cutover (same commit)

**Delete:**
- `ProjectCeres/Views/Categories/{Index,Create,Edit,Deactivate}.cshtml`
- `ProjectCeres/ViewModels/CategoryCreateViewModel.cs`
- `ProjectCeres/ViewModels/CategoryEditViewModel.cs`
- The throwing methods on `CategoryService` used only by Razor: `CreateAsync(CategoryCreateViewModel)`, `UpdateAsync(CategoryEditViewModel)`, `DeactivateAsync(Guid)`. Their `ICategoryService` declarations.
- Test methods in `CategoryServiceTests.cs` that exercise the throwing methods. Keep tests for `GetAllAsync`, `GetByIdAsync`, and the `Try*` methods.

**Keep:**
- `CategoriesController.cs` slimmed to:
  ```csharp
  public class CategoriesController : Controller
  {
      public IActionResult Index() => Redirect("/app/categories");
      public IActionResult Create() => Redirect("/app/categories/new");
      public IActionResult Edit(Guid id) => Redirect($"/app/categories/{id}/edit");
      public IActionResult Deactivate(Guid id) => Redirect("/app/categories");
  }
  ```
  All 302, never 301.
- `ICategoryService.TryCreateAsync`, `TryUpdateAsync`, `TryDeactivateAsync`. The API path. Untouched.
- `CategoryCombobox` (used by Budgets/Movements forms) hits `/api/categories/active` — that endpoint is unchanged; Combobox keeps working.

**Verification gate before commit:**

1. `dotnet build` — must succeed.
2. `dotnet test` — must pass.
3. `pnpm test` in `ProjectCeres.Client` — must pass.
4. `pnpm build` — must succeed.
5. **Manual click-through:**
   - `/app/categories` renders the Expense tab with skeleton then real rows.
   - Switching to Income tab works.
   - Typing in the search box filters; clearing restores.
   - Toggle "Include archived" — verify no archived rows exist in seed data, but the hint text appears.
   - System rows render with badge + tooltip; no `⋯` menu visible.
   - Click `⋯ → Edit` on a user row, navigates to `/app/categories/:id/edit`, form pre-populates, Save round-trips, returns to list.
   - Click `⋯ → Archive…` on a user row with no transactions, confirm, row disappears (or appears as Archived when toggle is on).
   - Click `⋯ → Archive…` on a user category with transactions, confirm, toast shows the in-use error.
   - Visit `/Categories` and verify 302 → `/app/categories`. Same for `/Categories/Create`, `/Categories/Edit/<id>`.

## Out of scope

- Reactivating archived categories. The API exposes only `archive`, not `reactivate`. If/when needed, a separate API addition + UI follow-up.
- Reassign-then-archive flow. Surfaced in error toast text only; no in-page reassignment UI yet.
- Bulk operations (multi-select, bulk archive). Not in this commit.
- Mobile sticky filter bar. Aspirational; verify at implementation that `position: sticky` works inside `<main>`. Fall back to non-sticky if it doesn't.
- Reordering / favouriting categories. Out of scope.
- Search beyond name (e.g. by lifestyle tag). Single search field, name-only.

## Follow-ups

- The CategoryCombobox at `src/app/components/CategoryCombobox.tsx` is a separate consumer of `/api/categories/active`. Its tests / behaviour are not changed by this commit, but Note this for the eventual auth batch — when `ICurrentUserAccessor` swaps from sentinel to authenticated, the Combobox automatically scopes to the user.
- The system-category sort-to-bottom rule is implemented in `CategoriesLayout` (client-side), not in the API. If the API ever gets server-side sorting, revisit.
- Reactivation API + UI is the natural follow-up if archived categories accumulate and the user wants to undo.
