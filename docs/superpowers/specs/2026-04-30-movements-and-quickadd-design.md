# Movements + Quick-Add — Design

## 1. Goal

Port the Movements list page to the SPA with text search and the existing 3 filters (account, date range), wire the dashboard's `+` button to a real quick-add modal that creates transactions, transfers, and liability payments without leaving the current page, and adopt Sonner as the SPA-wide toast system.

## 2. Architecture

**Frontend.** New `src/app/pages/Movements.tsx` route at `/app/movements` with a filter bar (text search + account select + date range), paginated table (50/page, numbered), and inline `IsClearedSwitch` reused from `src/components/`. Filters and page state live in URL query params (`?q=&accountId=&from=&to=&page=`); `useApi` re-fetches automatically when the URL changes (its existing `AbortController` handles request concurrency for free). Quick-add modal at `src/app/components/QuickAddModal.tsx`, mounted from `TopBar` (already has the `+` button) and from a new "+ New" button in the Movements page header.

**Backend.** Extend `IMovementService` with a `q` parameter (search description + category name). Add a new `MovementsApiController` for the SPA's GET endpoint. Add three POST endpoints — `POST /api/transactions`, `POST /api/transfers`, `POST /api/liability-payments` — each in its own thin API controller (only the create action; the rest of CRUD ports in the next slice). Add two combobox helper endpoints — `GET /api/accounts/active` and `GET /api/categories/active`.

**Razor cleanup.** A 302 redirect from `/Movements` → `/app/movements` ships first; then `Views/Movements/Index.cshtml` is deleted and the MVC `MovementsController` is reduced to the redirect-only stub.

**Toast system.** Install Sonner via shadcn, mount `<Toaster />` once in `AppLayout`. Quick-add (and future features) call `toast.success(...)` / `toast.error(...)`.

## 3. Components

### 3a. Backend DTOs

`ProjectCeres/ViewModels/MovementsDtos.cs`:

```csharp
public record MovementListItemDto(
    Guid Id,
    string MovementType,        // "Transaction" | "Transfer" | "LiabilityPayment"
    DateOnly Date,
    decimal Amount,
    string CurrencyCode,
    string CurrencySymbol,
    string? Description,
    bool IsCleared,
    string? AccountName,                  // Transaction
    string? CategoryName,                 // Transaction
    string? CategoryTypeName,             // Transaction ("Income" / "Expense")
    string? SourceAccountName,            // Transfer
    string? DestAccountName,              // Transfer
    string? AssetAccountName,             // LiabilityPayment
    string? LiabilityAccountName);        // LiabilityPayment

public record MovementsPageDto(
    IReadOnlyList<MovementListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public record CreateTransactionRequest(
    DateOnly Date,
    decimal Amount,
    Guid AccountId,
    Guid CategoryId,
    string? Description);

public record CreateTransferRequest(
    DateOnly Date,
    decimal Amount,
    Guid SourceAccountId,
    Guid DestAccountId,
    string? Description);

public record CreateLiabilityPaymentRequest(
    DateOnly Date,
    decimal Amount,
    Guid AssetAccountId,
    Guid LiabilityAccountId,
    string? Description);

public record AccountOptionDto(
    Guid Id,
    string Name,
    string CurrencyCode,
    string CurrencySymbol,
    string AccountTypeName);

public record CategoryOptionDto(
    Guid Id,
    string Name,
    string CategoryTypeName);
```

### 3b. New API controllers

- `MovementsApiController` (`api/movements`): `GET` returning `MovementsPageDto`; query params `q`, `accountId`, `from`, `to`, `page`, `pageSize` (default 50).
- `TransactionsApiController` (`api/transactions`): `POST` creating a transaction. Only the create action this slice.
- `TransfersApiController` (`api/transfers`): `POST` creating a transfer. Only create.
- `LiabilityPaymentsApiController` (`api/liability-payments`): `POST` creating a liability payment. Only create.
- `AccountsApiController` (`api/accounts/active`): `GET` returning `AccountOptionDto[]` for active accounts.
- `CategoriesApiController` (`api/categories/active`): `GET` returning `CategoryOptionDto[]` for active categories.

### 3c. Service-layer change

`IMovementService.GetRecentAsync` and `CountAsync` get a `string? q` parameter. Implementation: `WHERE (description ILIKE '%q%' OR category.name ILIKE '%q%')`. Null-safe: when `q` is null/empty, the filter is skipped entirely.

### 3d. Frontend feature folder

`src/app/features/movements/`:

- `movements-api.ts` — DTO types (`MovementsPageDto`, `MovementListItemDto`, `CreateTransactionRequest`, `CreateTransferRequest`, `CreateLiabilityPaymentRequest`, `AccountOptionDto`, `CategoryOptionDto`) + URL constants (`MOVEMENTS_URL`, `TRANSACTIONS_CREATE_URL`, `TRANSFERS_CREATE_URL`, `LIABILITY_PAYMENTS_CREATE_URL`, `ACCOUNTS_ACTIVE_URL`, `CATEGORIES_ACTIVE_URL`).
- `MovementsTable.tsx` — pure table component; props: `items`. Renders rows with type badge, accounts, category/details, description, signed amount, `IsClearedSwitch`. No edit/delete actions this slice.
- `MovementsFilterBar.tsx` — text search input (debounced 300ms via local state → URL update), account select, from/to date inputs. Reads/writes URL params; emits no events (URL is the source of truth).
- `MovementsPagination.tsx` — page links (1, 2, …, last); active page highlighted; reads `?page=` from URL.

### 3e. Page component

`src/app/pages/Movements.tsx`:

- Reads search params via `useSearchParams` (React Router).
- Builds URL from params; calls `useApi<MovementsPageDto>(url)`.
- Renders: heading "Movements" + "+ New" button, `<MovementsFilterBar>`, loading skeleton / `<CardError>` / empty state / `<MovementsTable>` + `<MovementsPagination>`.
- Owns `quickAddOpen` state; passes `onSaved={refetch}` to the modal so the list refreshes on success.

### 3f. Quick-add modal

`src/app/components/QuickAddModal.tsx`:

- Props: `open`, `onOpenChange`, `onSaved?: () => void`.
- Uses `<Dialog>` from shadcn.
- Three top-level tabs: **Transaction** | **Transfer** | **Liability Payment**.
- Per-tab field set:
  - Transaction: Date, Amount, Account ▾, Category ▾, Description.
  - Transfer: Date, Amount, Source account ▾ (any active account), Destination account ▾, Description.
  - Liability Payment: Date, Amount, Asset account ▾ (filtered to non-Liability accounts), Liability account ▾ (filtered to Liability accounts), Description.
- Plain `useState` for fields (no `react-hook-form` this slice).
- Combobox-based account and category selectors using shadcn `Command` + `Popover`.
- Currency symbol auto-derived from selected account, displayed as a prefix on the amount input.
- Inline field errors below each field on submit failure (mapped from server `ModelState`).
- On success: `toast.success("Saved.")`, close modal, call `onSaved?.()`.
- Submit button shows spinner + disabled during submit; re-enables on error.

### 3g. Settings consumption (currency on amount input)

The `AccountOptionDto` from `GET /api/accounts/active` carries `currencySymbol` per account; the modal uses that directly. No `useSettings` hook needed for this slice.

### 3h. Combobox helper endpoints

`GET /api/accounts/active` returns `AccountOptionDto[]` with `id`, `name`, `currencyCode`, `currencySymbol`, `accountTypeName` (so the Liability Payment tab can filter dropdowns by account type). Active accounts only.

`GET /api/categories/active` returns `CategoryOptionDto[]` with `id`, `name`, `categoryTypeName`. Active categories only.

These are intentionally small — they exist for combobox dropdowns, not for the future Accounts/Categories CRUD pages (which will have richer list endpoints).

### 3i. TopBar wiring

`src/app/layout/TopBar.tsx`:

- Replace the existing placeholder `Dialog` with `<QuickAddModal>`.
- TopBar doesn't pass `onSaved` (used cross-page). Cross-page auto-refresh of dashboard cards is deferred.

### 3j. Movements page wiring

The Movements page renders the same `<QuickAddModal>` from its "+ New" button and passes `onSaved={refetch}` so the list refreshes on success.

### 3k. AppLayout adds `<Toaster />`

`src/app/layout/AppLayout.tsx` mounts `<Toaster />` from Sonner once at the root.

## 4. Layout

### 4a. Movements page (`/app/movements`)

```tsx
<div className="space-y-6">
  <div className="flex items-center justify-between">
    <h1 className="text-3xl font-semibold">Movements</h1>
    <Button onClick={() => setQuickAddOpen(true)}>
      <Plus className="h-4 w-4" />
      New
    </Button>
  </div>

  <MovementsFilterBar />

  {loading && <Skeleton className="h-[400px] w-full" />}
  {error && <CardError section="Movements" onRetry={refetch} />}
  {data && data.items.length === 0 && (
    <p className="text-sm text-muted-foreground">No movements found.</p>
  )}
  {data && data.items.length > 0 && (
    <>
      <MovementsTable items={data.items} />
      <MovementsPagination totalCount={data.totalCount} pageSize={data.pageSize} />
    </>
  )}

  <QuickAddModal open={quickAddOpen} onOpenChange={setQuickAddOpen} onSaved={refetch} />
</div>
```

### 4b. Filter bar

Single horizontal row on desktop, stacked on mobile. Search input takes flex-1, the rest are fixed-width:

```
[🔍 Search description or category........]  [Account ▾]  [From 📅]  [To 📅]  [Clear]
```

The "Clear" button only shows when at least one filter is active. No filter chips this slice.

### 4c. Table

Columns mirror Razor: Date, Type, Account(s), Category/Details, Description, Amount (right-aligned), Status (`IsClearedSwitch`).

Type badge colors using brand semantic tokens:
- Transaction: `bg-info/10 text-info`
- Transfer: `bg-chart-4/10 text-chart-4`
- Liability Payment: `bg-warning/10 text-warning`

Amount color via category type for transactions: Income green (`text-success`), Expense red (`text-destructive`). Transfers and Liability Payments render neutral (`text-foreground`).

No Edit/Delete columns this slice.

### 4d. Quick-add modal

Compact dialog (max-width ~28rem):

```
┌────────────────────────────────┐
│  Quick add              ✕      │
├────────────────────────────────┤
│  [Transaction] [Transfer] [Liab. Pay.] │  ← segmented tabs
│                                │
│  Date      [📅 ____________]   │
│  Amount    [€ ____________]    │
│  Account   [▾ ____________]    │  ← combobox
│  Category  [▾ ____________]    │  ← combobox (Transaction tab only)
│  Description [____________]    │
│                                │
│         [Cancel]  [Save]       │
└────────────────────────────────┘
```

Tab-specific account fields:
- Transaction: single Account combobox (any active account).
- Transfer: Source account + Destination account (two comboboxes stacked); both list active accounts.
- Liability Payment: Asset account + Liability account (two comboboxes); Asset is filtered to `accountTypeName != "Liability"`, Liability is filtered to `accountTypeName == "Liability"`.

## 5. Razor Cleanup & Migration

**Remove:**
- `ProjectCeres/Views/Movements/Index.cshtml`

**Modify:**
- `ProjectCeres/Controllers/MovementsController.cs` — replace `Index()` body with `Redirect("/app/movements")`.

**Add 302 redirect:**
- `/Movements` → `/app/movements`. Per the per-area pattern in `planning-phase3-spa-migration.md` (302 during migration; the global `/app/*` → `/*` 301 is later).

**Keep:**
- `IMovementService` / `MovementService` — extended with `q` parameter; consumed by the new `MovementsApiController`.
- `IsClearedSwitch.tsx` in `src/components/` — still used by Razor Transactions/Transfers Index pages until those migrate.
- The Transactions/Transfers/LiabilityPayment full Razor CRUD pages — untouched this slice.

**New routes added to React Router:**
- `/movements` → `<Movements />` page.

## 6. Data Flow & Error Handling

**Movements page:**
- `useSearchParams` reads URL → builds query string → `useApi<MovementsPageDto>(url)`.
- URL changes (filter typed, page clicked) → effect re-runs → previous request's `AbortController.abort()` cancels the in-flight request → new request fires.
- Loading → `<Skeleton>`; error → `<CardError>`; empty → muted text; data → table + pagination.

**Quick-add modal:**
- Submit → POST to `/api/transactions` | `/api/transfers` | `/api/liability-payments` based on active tab.
- 200/201 → `toast.success("Saved.")`, close modal, call `onSaved?.()`.
- 400 (validation error from `ModelState`) → map field errors to inline messages below each field; modal stays open.
- Other errors → `toast.error("Couldn't save. Try again.")`.
- During submit → submit button shows spinner + disabled.

**IsCleared toggle:**
- Click → existing `IsClearedSwitch` PATCH (no behavioral change).
- On error → `toast.error("Couldn't update status.")` (small upgrade to the existing component).

## 7. Testing Strategy

**Backend (xUnit + FluentAssertions):**
- `MovementsApiController` GET: assert wrapper shape (`items`, `totalCount`, `page`, `pageSize`); assert `q` filters by description AND category name; assert pagination math (offset = `(page - 1) * pageSize`); assert account/date filters still work.
- `MovementService` unit test: `q` parameter narrows results; null/empty `q` returns unfiltered.
- `TransactionsApiController.Create`: 201 on valid; 400 with `ModelState` on invalid (missing required, amount ≤ 0); creates a transaction in DB.
- `TransfersApiController.Create`: same pattern; 400 if source == destination; 400 if cross-currency.
- `LiabilityPaymentsApiController.Create`: same pattern; 400 if asset/liability accounts have wrong type.
- `/Movements` 302 redirect test.
- `GET /api/accounts/active`: shape + only-active filtering.
- `GET /api/categories/active`: shape + only-active filtering.

**Frontend (Vitest + RTL):**
- `Movements.tsx`: loading skeleton, error state, empty state, populated table; URL param round-trip.
- `MovementsFilterBar.tsx`: typing in search updates URL after debounce; account select updates URL; date inputs update URL; "Clear" empties all params.
- `MovementsPagination.tsx`: renders correct page links; active page highlighted; clicking a page updates `?page=`.
- `QuickAddModal.tsx`: tab switching shows correct fields; submit calls correct endpoint per tab; `toast.success` fires on success; inline errors render on 400; comboboxes filter correctly (Asset ≠ Liability for the Liability Payment tab).

## 8. Documentation Updates

- `docs/api-contract.md` — add rows for `GET /api/movements`, `POST /api/transactions`, `POST /api/transfers`, `POST /api/liability-payments`, `GET /api/accounts/active`, `GET /api/categories/active`.
- `docs/planning-phase3-spa-migration.md` — mark `MovementsController` as migrated; note 302 redirect live; note that Transactions/Transfers/LiabilityPayments controllers gained a `Create` API action but still serve their full Razor CRUD until their own migration.
- `docs/planning-phase3.md` — record progress under item 6 (Movements + Transactions + Transfers): Movements + quick-add slice complete; full CRUD for Transactions/Transfers still pending.
- `docs/design-system.md` — add Sonner toast usage notes (mount point in `AppLayout`, when to use `success` vs `error`, recommended messages).

## 9. Out of Scope (logged for follow-ups)

- **Per-table search & saved searches** (master spec §3) — full filter bar with chips, "Save search", saved-search dropdown. Its own brainstorm next.
- **Recently-used in comboboxes** (master spec §4) — needs a recents tracking layer (server preference or localStorage). Defer until a slice that needs it.
- **Cleared status filter** in Movements — lands with the full filter bar redesign.
- **Edit / Delete actions** on Movements rows — port with the Transactions/Transfers full CRUD slice.
- **Cross-page refetch on quick-add** (dashboard MTD/Health auto-refresh) — deferred. Likely lands with TanStack Query (already logged in `planning-future.md`).
- **`react-hook-form` + `zod`** — adopt with the full Transactions/Transfers form slice.
- **Settings-aware formatting** — already logged in `planning-future.md`. Movements uses `Intl` defaults for now.
