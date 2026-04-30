# Movements CRUD — Spec

> **Status:** Draft, ready for review (2026-04-30).
> **Phase:** 3 — SPA Migration, step 6 ("Movements + Transactions + Transfers — full CRUD").
> **Predecessor:** Movements list + quick-add (shipped 2026-04-30, plan `2026-04-30-movements-and-quickadd.md`).
> **Successor:** Per-table search & saved searches (separate brainstorm, not yet specced).
> **Scope boundary:** This spec covers Create / Edit / Delete for Transactions, Transfers, and Liability Payments — all reached through the unified Movements surface at `/app/movements*`. It also covers attachments (two-phase upload), bulk-cleared, and CSV export, and the Razor demolition for the affected controllers. It does **not** cover Accounts, Categories, Budgets, Recurring (step 7), Reports (step 8), or saved searches.

---

## 1. Goals

1. Replace the Razor full-form Create / Edit / Delete pages for Transactions and Transfers (and add equivalents for Liability Payments, which never had standalone Razor pages) with a single SPA surface under `/app/movements*`.
2. Keep the daily-driver quick-add flow untouched as the global fast path; the new Create page is the home for the long form (attachments, future fields).
3. Move the two cross-cutting Razor-only behaviours (`BulkMarkCleared`, CSV export) onto the Movements page so we can delete the Razor `TransactionsController.Index` cleanly.
4. Reduce the affected Razor MVC actions to 302 redirects and delete their views in a single demolition pass at the end of the slice.

## 2. Non-goals

- No dedicated Index pages for Transactions or Transfers in the SPA — Movements is *the* list.
- No saved searches, multi-select filters, or per-table search — those land in the next brainstorm.
- No global-search behaviour changes — surface filtering of movement results from `⌘K` is noted as a follow-up.
- No animation polish on the list ↔ form transition. CSS hooks are placed for a future PR.
- No attachments for Liability Payments — there is no `LiabilityPaymentAttachment` entity today and adding one is out of scope. Spec only covers Transaction and Transfer attachments.

## 3. Decisions (locked in brainstorm 2026-04-30)

1. No dedicated Transactions/Transfers Index pages. Movements is the single list.
2. Movements gains a single-select **type filter**: `All / Transactions / Transfers / Liability payments`. URL-param-driven (`type=transaction|transfer|liabilitypayment`), async (no full reload).
3. Edit lives at `/app/movements/:id/edit` as a routed page. Movements list stays mounted via `<Outlet />` so the back navigation feels like "sliding back to the table." View-transition CSS hooks (`view-transition-name`) are present; the animation itself is a deferred follow-up.
4. Delete is reachable from **two** places, both confirmed by an `AlertDialog`:
   - The Edit page's danger zone (footer).
   - A row-level `⋯` dropdown on Movements (Edit / Delete).
5. Create lives at `/app/movements/new` (full form) with `?type=transaction|transfer|liabilitypayment` for deep-linking. The Create page and Edit page share one `MovementForm` component rendered in two modes.
6. Attachments use a **two-phase, save-first** upload: the row saves as JSON, then each file uploads individually to `POST /api/transactions/:id/attachments` (or the transfer equivalent). The dropzone works against an existing row on the Edit page too — no resave required to add a receipt later.
7. Quick-add (button + keyboard shortcut) is suppressed on `/app/movements*`. Stays global elsewhere. `⌘K` global search is untouched on every route.
8. Edit page exposes `Cleared` as a `Switch` field, consistent with the Movements row toggle.
9. `BulkMarkCleared` moves to Movements, scoped to the current filter set (date range + account + type). Free-text `q` is intentionally excluded from bulk operations — see §5.1.
10. CSV Export moves to Movements, scoped to the current filter set.
11. Razor `TransactionsController` and `TransfersController` actions for Index / Create / Edit / Delete / Export / BulkMarkCleared are reduced to 302 redirects at the end of the slice. Their Razor views are deleted in the same commit.

## 4. Routes (React Router)

All routes are nested under the existing app shell at `/app/*`.

```
/app/movements                       → Movements list (existing) — gains type filter, row ⋯ menu, bulk actions
/app/movements/new                   → Create page; renders MovementForm in "create" mode with a type picker
/app/movements/new?type=transaction  → Create, with the type picker pre-selected
/app/movements/new?type=transfer     → ditto
/app/movements/new?type=liabilitypayment → ditto
/app/movements/:id/edit              → Edit page; renders MovementForm in "edit" mode for the resolved type
```

Implementation note: `MovementsLayout` (the routed component for `/app/movements`) renders the Movements list as its own content **and** an `<Outlet />`. The `new` and `:id/edit` children render into that outlet, mounting *over* the list rather than replacing it. Closing the form (Save, Delete, Cancel, browser back) returns to the list with scroll/filter state preserved because the list never unmounted.

CSS view-transition hooks (no animation behaviour today, just named layers for a future polish PR):

- Each Movements row: `view-transition-name: movement-row-{id}` (computed inline).
- The form container on Create/Edit: `view-transition-name: movement-form`.

## 5. API surface

### 5.1 New endpoints (this slice)

| Verb     | Path                                          | Body / Query                                                                              | Response                          | Notes |
|----------|-----------------------------------------------|-------------------------------------------------------------------------------------------|-----------------------------------|-------|
| `GET`    | `/api/transactions/{id}`                      | —                                                                                         | `200` `TransactionEditDto`         | 404 if not found. |
| `PUT`    | `/api/transactions/{id}`                      | `UpdateTransactionRequest`                                                                | `204`                              | 422 on validation. |
| `DELETE` | `/api/transactions/{id}`                      | —                                                                                         | `204`                              | Hard delete. 404 if not found. |
| `POST`   | `/api/transactions/{id}/attachments`          | `multipart/form-data` `file`                                                              | `201` `{ id, fileName, sizeBytes, contentType }` | One file per request. |
| `DELETE` | `/api/transactions/attachments/{attachmentId}` | —                                                                                        | `204`                              | Hard delete. |
| `GET`    | `/api/transfers/{id}`                         | —                                                                                         | `200` `TransferEditDto`            | 404 if not found. |
| `PUT`    | `/api/transfers/{id}`                         | `UpdateTransferRequest`                                                                   | `204`                              | 422 on validation incl. cross-field. |
| `DELETE` | `/api/transfers/{id}`                         | —                                                                                         | `204`                              | Hard delete. |
| `POST`   | `/api/transfers/{id}/attachments`             | `multipart/form-data` `file`                                                              | `201` `{ id, fileName, sizeBytes, contentType }` | One file per request. |
| `DELETE` | `/api/transfers/attachments/{attachmentId}`   | —                                                                                         | `204`                              | Hard delete. |
| `GET`    | `/api/liability-payments/{id}`                | —                                                                                         | `200` `LiabilityPaymentEditDto`    | 404 if not found. |
| `PUT`    | `/api/liability-payments/{id}`                | `UpdateLiabilityPaymentRequest`                                                           | `204`                              | 422 on validation. |
| `DELETE` | `/api/liability-payments/{id}`                | —                                                                                         | `204`                              | Hard delete. |
| `POST`   | `/api/movements/bulk-cleared`                 | `{ from, to, accountId?, type? }`                                                         | `200` `{ cleared: int }`            | Marks all matching movements cleared. Reuses the same filter shape as `GET /api/movements`. `q` is **not** accepted — bulk by free-text search is too risky. |
| `GET`    | `/api/movements/export.csv`                   | Same query params as `GET /api/movements` (`q`, `accountId`, `from`, `to`, `type`)        | `200 text/csv`                     | Streamed; no pagination — the export is the full filtered set. |

### 5.2 Modified endpoints

| Verb  | Path             | Change                                                                                          |
|-------|------------------|-------------------------------------------------------------------------------------------------|
| `GET` | `/api/movements` | Add `type` query parameter accepting `transaction \| transfer \| liabilitypayment`. Omitted = all (current behaviour). Invalid value = 400 with `INVALID_TYPE`. |

### 5.3 DTO shapes

`TransactionEditDto` — mirrors `TransactionEditViewModel`'s field set, JSON-shaped. Same fields as `CreateTransactionRequest` plus `id` and `isCleared`. Date as ISO `YYYY-MM-DD`.

`TransferEditDto` — `id`, `date`, `amount`, `sourceAccountId`, `destAccountId`, `description`, `isCleared`.

`LiabilityPaymentEditDto` — `id`, `date`, `amount`, `assetAccountId`, `liabilityAccountId`, `description`, `isCleared`.

`UpdateXRequest` shapes are identical to the corresponding `CreateXRequest`, plus an explicit `isCleared` field (PUT is a full replacement of editable state).

Attachment list on each Edit DTO: `attachments: [{ id, fileName, sizeBytes, contentType, uploadedAt }]`. Liability Payment DTO has no `attachments` field per non-goals.

### 5.4 Validation

All write endpoints rely on `[ApiController]` + `InvalidModelStateResponseFactory` for ModelState → 422 with the standard `error.details[]` shape (see `api-contract.md`). Cross-field rules return explicit 422s following the existing TransfersApi pattern:

- Transfer: source ≠ destination, source/destination currencies must match.
- Liability Payment: asset account must be Asset type; liability account must be Liability type; currencies must match; date ≥ both accounts' opening balance dates (existing service throws `InvalidOperationException`; controller catches and returns 422).

## 6. UI surface

### 6.1 Movements list — additions to existing page

- **Type filter dropdown.** New control on `MovementsFilterBar`, single-select shadcn `Select` with options `All / Transactions / Transfers / Liability payments`. Bound to URL param `type`. Clearing resets to `All` (no `type` param). Resets `page=1` on change like the existing filters.
- **Row actions menu.** Each table row gains a trailing `⋯` cell with a `DropdownMenu`: `Edit` (navigates to `/app/movements/:id/edit`), `Delete` (opens `AlertDialog`). Keyboard accessible. Replaces "click row to navigate" — clicking the row body still opens Edit, but the explicit menu is the discoverable affordance.
- **Page header changes.** The current `+` quick-add button is replaced with `+ New` that routes to `/app/movements/new`. The local `QuickAddModal` instance and its state are removed from `Movements.tsx`.
- **Bulk and export buttons.** A secondary action group on the page header: `Mark visible cleared` (opens a confirm dialog summarising the active filter, calls `POST /api/movements/bulk-cleared`) and `Export CSV` (triggers a download from `GET /api/movements/export.csv` with the active query string). Both are disabled when the filter would produce zero rows.

### 6.2 Create page (`/app/movements/new`)

- **Type picker landing.** When no `?type=` is set, the page renders three cards in a row: Transaction / Transfer / Liability Payment. Selecting one swaps in the relevant `MovementForm` mode and updates the URL to `?type=…` so refresh / back works.
- **Once a type is picked**, renders `<MovementForm mode="create" type={type} />`.
- **Footer:** `Cancel` (back to `/app/movements`) and `Save` (primary). No Delete here.
- **Attachments:** dropzone is disabled on Create until first save, with a tooltip ("Save first, then drop receipts"). On successful save, the page redirects (with `replace: true`) to the Edit page (`/app/movements/:id/edit?created=1`). Because the Movements list is mounted under the same `<Outlet />` parent on both routes, this redirect feels seamless to the user — no list refetch, no scroll loss. The user can immediately drop receipts on the Edit page. This is the only place the two-phase save shows up as visible UX.

### 6.3 Edit page (`/app/movements/:id/edit`)

- **Resolves type from the row's `movementType`.** The page first looks at the Movements list cache (most users land here from the list) and, if absent (e.g., deep link), issues one `GET /api/movements?...` matched by id, or — simpler — calls a small new helper `GET /api/movements/{id}` returning `{ movementType }` so we know which typed endpoint to hit. **Decision:** add `GET /api/movements/{id}` returning `{ id, movementType }` purely as a type-discriminator; the typed `GET /api/transactions/{id}` etc. follow.
- Renders `<MovementForm mode="edit" type={type} initialData={dto} />`.
- **Cleared switch:** form field bound to `isCleared`. Save round-trips it via the typed PUT endpoint.
- **Attachments dropzone:** active. Drop a file → POST to the right endpoint → optimistic add to the list with a progress bar → confirm or rollback. Existing attachments listed with an `×` to delete (confirmed via `AlertDialog`).
- **Footer:** `Cancel`, `Save`, plus a `Danger zone` block with a `Delete movement` button that opens an `AlertDialog`.

### 6.4 `MovementForm` component contract

```ts
type MovementFormProps =
  | { mode: 'create'; type: MovementType; defaults?: Partial<FormState> }
  | { mode: 'edit';   type: MovementType; initialData: MovementEditDto };
```

- One component, three internal renderings keyed by `type` (Transaction / Transfer / LiabilityPayment).
- Validation: client-side via the same Field/Form helpers used elsewhere in the SPA. Server-side errors mapped from 422 `error.details[]` to per-field messages — same pattern as quick-add.
- Submit:
  - `create` → POST to the typed create endpoint → on 201, navigate to `/app/movements/:id/edit?created=1` with `replace: true`. The `?created=1` flag triggers a `Sonner` toast on the Edit page.
  - `edit` → PUT to the typed edit endpoint → on 204, stay on the page, show toast, mark form pristine.
- Attachments dropzone is mounted *outside* the form's submit button — its uploads do not block save, and save does not flush them.

### 6.5 Quick-add suppression on `/app/movements*`

`TopBar` reads the current pathname. While it matches `^/app/movements(/|$)`, the `+` button is hidden and the keyboard shortcut handler returns early. No state change to the modal; just the trigger surface is suppressed. `⌘K` and the rest of the TopBar are unaffected.

## 7. Data flow — happy paths

### 7.1 Create a transaction with a receipt

1. User clicks `+ New` on `/app/movements`.
2. Lands on `/app/movements/new` → picks `Transaction` → URL becomes `/app/movements/new?type=transaction`.
3. Fills the form, drops a receipt onto the dropzone — the dropzone tooltip says "Save first." File is held client-side as a pending upload.
4. Hits `Save`. `POST /api/transactions` → 201 with `{ id }`.
5. Page navigates (`replace: true`) to `/app/movements/:id/edit?created=1`. Toast: "Transaction created."
6. The Edit page picks up the pending file from a small in-memory hand-off (a route-level `useLocation().state`), starts the upload to `POST /api/transactions/:id/attachments`, shows a per-file progress row.
7. On 201 the attachment is added to the list. Toast: "Receipt uploaded."

### 7.2 Edit and delete a transfer

1. User clicks `⋯ → Edit` on a transfer row.
2. Lands on `/app/movements/:id/edit`. Form prefilled from list cache; if cache miss, `GET /api/movements/{id}` → `GET /api/transfers/{id}`.
3. User flips Cleared, hits Save. `PUT /api/transfers/{id}`. Toast.
4. User clicks `Delete movement` in danger zone. `AlertDialog` → confirm. `DELETE /api/transfers/{id}`. Navigate back to `/app/movements`. Toast: "Transfer deleted."

### 7.3 Bulk mark cleared

1. User on `/app/movements` filters to `from=2026-03-01, to=2026-03-31, accountId=…, type=transaction`.
2. Clicks `Mark visible cleared`. Confirm dialog: "Mark 47 transactions as cleared?" (count comes from the existing Movements page count).
3. `POST /api/movements/bulk-cleared` with the same filter shape. 200. Movements list refetches.

## 8. Razor demolition map (end of slice)

When SPA Edit/Create/Delete are live and verified end-to-end, in a single commit:

- `TransactionsController.Index` → `Redirect("/app/movements")` (302).
- `TransactionsController.Create` (GET) → `Redirect("/app/movements/new?type=transaction")`.
- `TransactionsController.Edit` (GET) → `Redirect($"/app/movements/{id}/edit")`.
- `TransactionsController.Delete` (GET) → `Redirect($"/app/movements/{id}/edit")`.
- `TransactionsController.Export` (GET) → `Redirect("/app/movements")` (the new export lives there).
- `TransactionsController.BulkMarkCleared` (POST) → leave the action in place but stop linking to it from any view; mark `[Obsolete]`. Removed in the final cleanup plan.
- `TransactionsController.ToggleCleared` (POST) → same — already redundant with `PATCH /api/movements/{id}/cleared`. Mark `[Obsolete]`, remove in final cleanup.
- `TransfersController.Index / Create / Edit / Delete` → equivalent 302s.
- `Views/Transactions/*.cshtml` and `Views/Transfers/*.cshtml` deleted (Index, Create, Edit, Delete views and any partials).
- `TransactionsController.cs` and `TransfersController.cs` POST endpoints for ModelState-bound Edit/Create/Delete → deleted (their bodies no longer reachable). Only the GET 302 actions remain.

`AttachmentsController` (Razor download/delete) stays for now — it serves files to the Razor side. Eventually the SPA-side download switches to a JSON-then-blob endpoint, but that's its own cleanup.

## 9. Testing approach

### 9.1 Server (xUnit)

- **Controller tests** for each new endpoint following the existing `Controllers/Api/` test pattern. Cover: 200/201/204 happy path, 404 on missing id, 422 on ModelState and cross-field violations, 400 on `type=` invalid value.
- **Service-layer tests** unchanged — services already have `Update`/`Delete`/`MarkCleared` and existing tests for them. The `MovementService` type filter gets a new test covering each filter value plus `null`.
- **Bulk-cleared** test asserts that the filter shape (`from`, `to`, `accountId?`, `type?`) routes to the right service calls and returns the count.

### 9.2 Client (Vitest + React Testing Library)

- `MovementsFilterBar` — type filter renders, syncs to URL param, resets `page=1`.
- `MovementsTable` row `⋯` menu — opens, both items keyboard-reachable, Delete confirm fires the right endpoint.
- `MovementForm` — three render branches; create/edit modes; Cleared switch round-trip; client validation; 422 mapping to per-field errors.
- `Create.tsx` — type picker behaviour; `?type=` URL sync; redirect to Edit on success.
- `Edit.tsx` — type discriminator; danger zone deletes; pending-attachment hand-off from Create.
- `AttachmentDropzone` — happy upload, per-file progress, deletion confirm.
- Suppression test: TopBar `+` button is hidden on `/app/movements*` routes.

### 9.3 Manual checklist

Browser smoke after the Razor demolition commit:

- `/Transactions` → `/app/movements` (302).
- `/Transactions/Create` → `/app/movements/new?type=transaction`.
- `/Transactions/{id}/Edit` → `/app/movements/{id}/edit`.
- `/Transfers` → `/app/movements`.
- `/Transfers/Create` → `/app/movements/new?type=transfer`.
- `/Transfers/{id}/Edit` → `/app/movements/{id}/edit`.
- Quick-add unreachable from Movements pages (button hidden, shortcut no-op).
- `⌘K` works on Movements pages.
- CSV export downloads exactly the filtered set.
- Bulk-cleared count matches the visible filter.

## 10. Documentation updates

- `docs/planning-phase3.md` — under §14 step 6, mark "Full Transactions/Transfers/LiabilityPayments CRUD" as ✓ migrated, with date and a one-liner pointer to this spec and the implementation plan.
- `docs/planning-phase3-spa-migration.md` — update §2 controller table (Transactions, Transfers rows), §5 React Router route map (drop standalone `/app/transactions*` and `/app/transfers*` routes, add `/app/movements/new` and `/app/movements/:id/edit`).
- `docs/api-contract.md` — extend the endpoint reference with the new typed endpoints and `/api/movements/bulk-cleared` and `/api/movements/export.csv`.
- `docs/changelog.md` — entry under `[Unreleased]` per `changelog-sync` skill.

## 11. Out of scope, follow-ups

1. **Saved searches & per-table search** — separate brainstorm. URL-param-based filtering remains the contract for now.
2. **Animated list ↔ form transition** — view-transition CSS hooks ship in this slice; the actual animation polish is a follow-up plan, gated on real beta usage.
3. **Global `⌘K` search behaviour while on Movements** (your suggestion to filter out movement results, since the user is already in that context) — folded into the search/saved-searches brainstorm. Not actioned here.
4. **Liability Payment attachments** — no entity today; revisit when LiabilityPayment usage data justifies it.
5. **`AttachmentsController` (Razor)** retirement — delete after the SPA-side download path is migrated; tracked in the final SPA cleanup plan, not here.
6. **`[Obsolete]` Razor POST endpoints removal** (`TransactionsController.BulkMarkCleared`, `ToggleCleared`) — final SPA cleanup plan.

## 12. Risks & open notes

- **Pending-attachment hand-off via `useLocation().state`** is fragile across hard refresh — if the user reloads `/app/movements/:id/edit?created=1` the pending file is lost. Mitigation: only offer the auto-upload once, and if the hand-off is missing show "Drop your receipt here" with no error. Acceptable behaviour for the beta.
- **Type filter performance:** today `MovementService` queries all three sources and unions in memory. Filtering by type at the application level (skip the queries we don't need) is a small optimisation — worth doing in this slice while we're touching the service.
- **CSV export** ships with no streaming chunked encoding for now (synchronous build of the response body). A 50k-row export would block the request thread. For invite-only beta data volumes this is fine; revisit if we see it.
- **404 on the type discriminator endpoint (`GET /api/movements/{id}`)** — needs to be cheap because Edit deep-links go through it. Implementation can be three parallel `Exists`-style queries, take the first hit; or a single `UNION` SQL query. Plan should pick one.

---

## 13. Implementation order (for the plan)

1. Server: type filter on `GET /api/movements`. Test.
2. Server: typed `GET /:id`, `PUT /:id`, `DELETE /:id` for Transactions, Transfers, Liability Payments. Tests per controller.
3. Server: `GET /api/movements/{id}` discriminator. Test.
4. Server: `POST /api/movements/bulk-cleared`. Test.
5. Server: `GET /api/movements/export.csv`. Test.
6. Server: `POST /api/transactions/{id}/attachments`, `POST /api/transfers/{id}/attachments`, `DELETE` equivalents. Tests.
7. Client: type filter dropdown wired into `MovementsFilterBar`. Test.
8. Client: row `⋯` menu on `MovementsTable` (Edit, Delete + confirm). Test.
9. Client: bulk-cleared and export buttons on Movements page header. Test.
10. Client: `<Outlet />` plumbing on `/app/movements`, `MovementForm` shared component, Create page (type picker + create flow), Edit page (loads, saves, danger zone delete). Tests.
11. Client: `AttachmentDropzone` + Create→Edit pending-file hand-off. Tests.
12. Client: TopBar quick-add suppression on `/app/movements*`. Test.
13. View-transition CSS hooks (no behaviour change, no tests beyond "it compiles").
14. Razor demolition: 302s, view deletions, link audit. Manual smoke.
15. Docs sync per §10.

Each step is independently shippable and reversible.
