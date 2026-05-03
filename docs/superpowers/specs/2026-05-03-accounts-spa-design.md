# Accounts SPA — Design Spec

> **Status:** Approved 2026-05-03. Accounts is the third SPA page in Phase 3 Batch 2 of the frontend migration. Settings (commit `e842250`) locked the SPA-page template; Categories (commit `9578f9a` + follow-ups `132d5df`, `87f6709`) exercised the template against list + nested CRUD + archive lifecycle. This spec applies the established pattern to Accounts and adds page-specific design choices: per-account currency, conditional Asset/Liability fields, payoff projection for amortising liability accounts, and an adaptive archive AlertDialog.

---

## Index

1. [Goals & non-goals](#1-goals--non-goals)
2. [File structure & locked SPA-page conventions](#2-file-structure--locked-spa-page-conventions)
3. [Routing & navigation](#3-routing--navigation)
4. [List page (`/app/accounts`)](#4-list-page-appaccounts)
5. [Form (Create + Edit shared shape)](#5-form-create--edit-shared-shape)
6. [Row menu + Archive flow](#6-row-menu--archive-flow)
7. [Ledger page (`/app/accounts/:id/ledger`)](#7-ledger-page-appaccountsidledger)
8. [Server-side changes](#8-server-side-changes)
9. [Tests, verification gate, commit shape](#9-tests-verification-gate-commit-shape)
10. [Out of scope (logged elsewhere)](#10-out-of-scope-logged-elsewhere)
11. [Decision log](#11-decision-log)

---

## 1. Goals & non-goals

### Goals

- Replace the placeholder `/app/accounts` route with a real Accounts SPA: list, Create, Edit, Ledger.
- Mirror the Razor `AccountsController` feature set so no capability regresses on cutover.
- Apply the locked SPA-page template established by Settings and Categories. No deviation from the template; deviations are explicitly flagged in §2.
- Slim the Razor `AccountsController` to five 302 redirects matching the Categories cutover pattern. Delete the Razor views and the throwing-method service surface they used.
- Tighten one server invariant uncovered during the brainstorm: `AccountPolicies.ValidateLiabilityRepayment` rejects `repaymentType=null + interestRate≠null` (currently silent).
- Add `HasTransactions: bool` to `AccountListItemDto` to power adaptive archive AlertDialog copy.

### Non-goals

- **No new schema fields.** The `Notes` column mentioned in `docs/models.md` line 274 stays deferred (separate small plan later).
- **No changes to Movements behaviour.** Transactions for archived accounts still appear in Movements; the "Active accounts only" filter chip is logged to `planning-future.md` as a friction-driven follow-up.
- **No changes to Dashboard behaviour.** Archived accounts continue to contribute to net worth per the data model rule (`docs/models.md` lines 276–281). The "Includes archived accounts" tile tooltip is logged to `planning-future.md`.
- **No receivables / interest accrual on assets.** Logged as a Phase 4 candidate in `planning-future.md`.
- **No new ADR.** The policy extension in §8 is a symmetric tightening of an existing rule, not a new architectural concept.
- **No reactivate flow.** The Accounts API has no reactivate endpoint; the SPA does not invent one.

---

## 2. File structure & locked SPA-page conventions

### File structure (mirrors Categories)

**Create — client:**

```
ProjectCeres.Client/src/app/features/accounts/
  accounts-api.ts                   ← URL builders + DTOs (logic-free)
  AccountsLayout.tsx                ← list page glue
  AccountsLayout.test.tsx
  AccountsTable.tsx                 ← pure UI table
  AccountsTable.test.tsx
  AccountRowMenu.tsx                ← ⋯ menu + AlertDialog confirm
  AccountRowMenu.test.tsx
  AccountForm.tsx                   ← shared Create/Edit form
  AccountForm.test.tsx
  AccountCreate.tsx                 ← page glue for /new
  AccountCreate.test.tsx
  AccountEdit.tsx                   ← page glue for /:id/edit
  AccountEdit.test.tsx
  AccountLedger.tsx                 ← sibling top-level page
  AccountLedger.test.tsx
  AccountCurrencySubtotals.tsx      ← per-currency subtotal strip
  AccountCurrencySubtotals.test.tsx
```

**Modify — client:**

- `ProjectCeres.Client/src/app/pages/Accounts.tsx` — replace placeholder with one-line re-export `export { AccountsLayout as Accounts } from '../features/accounts/AccountsLayout';`
- `ProjectCeres.Client/src/app/App.tsx` — add nested routes (`/accounts/new`, `/accounts/:id/edit`) under the existing `accounts` route, plus a sibling top-level `/accounts/:id/ledger` route.

**Modify — server:**

- `ProjectCeres/Controllers/AccountsController.cs` — slim to 5 redirects.
- `ProjectCeres/Services/IAccountService.cs` — drop throwing CRUD method declarations (`CreateAsync(AccountCreateViewModel)`, `UpdateAsync(AccountEditViewModel)`).
- `ProjectCeres/Services/AccountService.cs` — drop the corresponding throwing method bodies.
- `ProjectCeres/Services/AccountPolicies.cs` — extend `ValidateLiabilityRepayment` to cover the None case (Layer 2 of the interest-rate normalisation).
- `ProjectCeres/ViewModels/AccountApiDtos.cs` — add `HasTransactions: bool` to `AccountListItemDto`.
- `ProjectCeres/Controllers/Api/AccountsApiController.cs` — populate `HasTransactions` in the list endpoint.
- `ProjectCeres.Tests/Integration/AccountServiceTests.cs` — drop tests for deleted throwing methods. Delete the file entirely if it becomes empty (Categories precedent).
- `ProjectCeres.Tests/Integration/Api/UserIdStampingTests.cs` — drop `RazorAccountService_*_stamps_UserId` if it exists (Categories precedent — that test guarded a now-removed Razor path).
- `ProjectCeres/Program.cs` — remove the `AddScoped<ILiabilityProjectionService, LiabilityProjectionService>()` line (the service is being deleted; see "Delete — server" below).

**Delete — server:**

- `ProjectCeres/Views/Accounts/Index.cshtml`
- `ProjectCeres/Views/Accounts/Create.cshtml`
- `ProjectCeres/Views/Accounts/Edit.cshtml`
- `ProjectCeres/Views/Accounts/Deactivate.cshtml`
- `ProjectCeres/Views/Accounts/Ledger.cshtml`
- `ProjectCeres/ViewModels/AccountCreateViewModel.cs`
- `ProjectCeres/ViewModels/AccountEditViewModel.cs`
- `ProjectCeres/ViewModels/AccountLedgerViewModel.cs` (already replaced by `AccountLedgerDto` in the API surface)
- `ProjectCeres/Services/ILiabilityProjectionService.cs` (zero callers post-cutover; projection math moves to the SPA per §7)
- `ProjectCeres/Services/LiabilityProjectionService.cs`
- `ProjectCeres/ViewModels/LiabilityProjectionViewModel.cs`
- `ProjectCeres.Tests/Unit/LiabilityProjectionServiceTests.cs` (tests the deleted service)
- `Program.cs` line registering `AddScoped<ILiabilityProjectionService, LiabilityProjectionService>()` (modification, not file delete — listed here for completeness)

### Locked SPA-page conventions (no deviation)

These are inherited from the Settings spec (`docs/superpowers/specs/2026-05-02-spa-page-pattern-and-settings-design.md`) and reaffirmed by the Categories spec. The Accounts spec replicates them exactly:

- `features/<area>/` folder with `<area>-api.ts` logic-free (URL builders + DTOs only).
- Page+Form split: pure UI components don't fetch or toast.
- `useApi` for GET, hand-rolled `fetch` for mutations.
- Popover+Command for any picker — never shadcn `<Select>`.
- `sonner` for toasts (mocked via `vi.mock('sonner', …)` in tests).
- Tests use `global.fetch = mockFetch as unknown as typeof fetch` — no MSW.
- AlertDialog (base-ui via `@/components/ui/alert-dialog`) for destructive confirms — not the legacy Razor-style `ConfirmDialog`.
- `Button render={<Link>...</Link>}` for link-as-button — never `asChild` (project's base-ui Button doesn't support `asChild`).
- Form button pair: `Save` (primary, default variant) + `Cancel` (variant="outline"). No Reset button — Reset solves no problem on a CRUD form (Categories session decision).

### Width

- List page: `max-w-3xl` (768px), centered. Matches Categories. Subtotal strip and list Card both inherit.
- Create / Edit forms: inherit `max-w-3xl` from layout.
- Ledger page: `max-w-4xl` (896px), centered. Wider than the list because the ledger has 5 columns and the Description cell needs room.

---

## 3. Routing & navigation

### Route structure (`App.tsx`)

```tsx
<Route path="accounts" element={<Accounts />}>
  <Route path="new" element={<AccountCreate />} />
  <Route path=":id/edit" element={<AccountEdit />} />
</Route>
<Route path="accounts/:id/ledger" element={<AccountLedger />} />
```

`<Accounts />` is the one-line re-export of `AccountsLayout`. Create and Edit mount inside the layout's `<Outlet>`. Ledger is a sibling top-level route — it has its own header, its own back-link, and does not share the list layout's chrome.

### `AccountsLayout` Outlet rendering rules

Same `childActive` pattern Categories established:

- When `useMatch('/accounts/new')` or `useMatch('/accounts/:id/edit')` is active → render only `<Outlet />` inside a `max-w-3xl` container; the layout's list chrome (header, search, Include archived toggle, "New account" button, table) is suppressed.
- Otherwise → render the full list page.
- The Outlet provides a context object `{ refetch: () => void }` so `AccountCreate` and `AccountEdit` can refresh the parent list cache after a successful mutation. Same shape Categories uses.

### Sibling Ledger route

`AccountLedger` does its own data fetch (`GET /api/accounts/:id/ledger`) and renders its own header. No shared state with `AccountsLayout`. Revisiting the list refetches; that cost is acceptable for a 4–15-row list.

### Navigation entry points

| Destination | Triggered from |
|---|---|
| `/accounts` | Sidebar nav item "Accounts"; "← Back to Accounts" link in Ledger header; post-Create / post-Edit redirect; Cancel button on Create/Edit forms |
| `/accounts/new` | "+ New account" button in list page header; first-run empty-state CTA |
| `/accounts/:id/edit` | `⋯ → Edit` in row menu (active rows only) |
| `/accounts/:id/ledger` | `⋯ → View ledger` in row menu (active and archived rows) |

### URL state (list page, search params)

- `?q=<search>` — debounced 200ms, client-side filter on Name. Drives the search input value.
- `?includeInactive=true` — toggled by the Include archived switch; absent when off.
- No `?type=` param — single mixed list, no Asset/Liability tabs (decided in Q6; reasoning in §11).

### URL state (Create / Edit / Ledger)

None. Each is identified by its path (`:id`).

---

## 4. List page (`/app/accounts`)

### Layout

```
┌────────────────────────────────────────────────────────────────────────┐
│ Accounts                                                                │  ← h1
│ Manage the accounts you own and the debts you owe. Balances are        │
│ derived from your transactions, transfers, and liability payments.     │
├────────────────────────────────────────────────────────────────────────┤
│ ┌────────────────────────────────────────────────────────────────────┐ │
│ │  EUR  €4,234.56     ·     USD  $890.00                             │ │  ← per-currency subtotal
│ └────────────────────────────────────────────────────────────────────┘ │     (≥2 currencies only)
│                                                                          │
│ ┌────────────────────────────────────────────────────────────────────┐ │
│ │  [Filter accounts…              ]   [+ New account]                │ │
│ │  ◯ Include archived                                                 │ │
│ │                                                                      │ │
│ │  Name                Type        Balance                            │ │
│ │  ──────────────────────────────────────────────                    │ │
│ │  Cash                Asset           €120.00            ⋯           │ │
│ │  Checking Account    Asset         €2,114.56            ⋯           │ │
│ │  Credit Card         Liability      €500.00 (red)       ⋯           │ │
│ │  Savings Account     Asset         €2,000.00            ⋯           │ │
│ └────────────────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────┘
```

### Header

- `<h1>` "Accounts" (text-2xl font-semibold). `tabIndex={-1}`, focused on mount via a ref + `useEffect` (Categories pattern; aids screen-reader and keyboard users on route change).
- One-line muted description below the h1: "Manage the accounts you own and the debts you owe. Balances are derived from your transactions, transfers, and liability payments."

### Per-currency subtotal strip (`AccountCurrencySubtotals`)

- Renders only when active accounts span 2+ distinct currencies. Single-currency users never see it.
- Inline list separated by middle dot. Each entry: `{Code} {Symbol}{NetAmount}` — e.g. `EUR €4,234.56 · USD $890.00`.
- **Net per currency formula:** `sum(asset balances in this currency) − sum(liability balances in this currency)`. Liability balances are stored as positive numbers but represent debt; subtracting them produces the user's actual net position per currency. (Naïvely summing all balances regardless of type would produce a meaningless number; the brainstorm caught this — see §11 Q7.)
- Active accounts only. Archived accounts are excluded from the strip even though they contribute to dashboard net worth — the strip reflects what's visible in the table.
- Container: small Card above the main list Card. Visually separate from the table chrome so it reads as a summary, not a header.

### Filter row (inside the list Card, above the table)

- Search Input — placeholder "Filter accounts…", debounced 200ms via the existing `useDebounced` hook, client-side filter via `account.name.toLowerCase().includes(query.toLowerCase())`.
- "+ New account" button — primary variant, `<Link to="new">` rendered via `<Button render={<Link>...</Link>}>`. Right-aligned in the flex row.
- "Include archived" Switch — separate row beneath the search/button row, sm-text label, drives `?includeInactive=true` on the URL and on the API URL via `buildListUrl(includeInactive)`.

### Table (`AccountsTable`)

Three columns + `⋯` slot:

| Column | Width | Alignment | Notes |
|---|---|---|---|
| Name | flex-1 | left | Inline `Archived` badge for archived rows |
| Type | w-32 | left | Renders "Asset" or "Liability" verbatim from `accountTypeName` |
| Balance | w-40 | right | `tabular-nums` font feature for column-aligned digits |
| `⋯` | w-12 | center | Row menu trigger; absent on archived rows that have no actions other than View ledger (see §6) |

**Sort:** alphabetical by Name, A-Z. Single sort. No user-toggleable sort.

**Archived row treatment:** `opacity-60` on the `<tr>` + inline `<Badge variant="secondary">Archived</Badge>` in the Name cell next to the name (gap-2 spacing). Same pattern Categories uses.

**Balance cell:**
- Right-aligned, `tabular-nums`.
- Color follows Q7 (B4) — color by **account type**, not by sign:
  - Asset accounts → `text-foreground` (normal). Negative balance (overdraft) renders as `-€200.00` in normal foreground; the minus sign is the signal.
  - Liability accounts → `text-destructive`. Negative balance (refund credit on a credit card after payoff) renders as `-€30.00` in red; the row is still a liability context.
- Format: `{Symbol}{Amount}` using the user's NumberFormat from Settings (e.g. `€1,234.56` for `eu`, `$1,234.56` for `us`). Currency code not shown in the cell — the symbol is enough; the row's currency is unambiguous in the per-currency subtotal strip above.

### Empty states (three branches)

| Branch | Detection | Render |
|---|---|---|
| First-run / truly zero accounts | Server returns empty list with `includeInactive=true` (no accounts exist at all) | Inside the list Card body, large centered area: heading "No accounts yet", body "Create your first account to start tracking.", primary `+ New account` button |
| Zero active accounts but archived ones exist | Default view (no `includeInactive`) returns empty, but `accounts.some(a => !a.isActive)` is true after fetching with the toggle | Italic muted text inside the table body: "No accounts." The Include archived toggle is already on screen — no extra hint needed |
| Search returned nothing | `sorted.length === 0 && query.length > 0` | Italic muted text: "No accounts match '{query}'." + ghost `Clear search` link button that resets the search input |

The first-run check requires a separate signal because the layout's `useApi` call is parameterised by `includeInactive`. Implementation: a second small fetch on mount to check the unfiltered count, OR include a `total` field on the list response. **Implementation choice deferred to the writing-plans step**; either is acceptable from a spec standpoint.

### Loading state

Four `<Skeleton>` rows (h-9, full width) inside the table area while `useApi` is loading. Same pattern Categories uses.

### Error state

`<CardError section="Accounts" onRetry={list.refetch} />` (existing component at `ProjectCeres.Client/src/app/components/CardError.tsx`).

---

## 5. Form (Create + Edit shared shape)

### Container

Both Create and Edit render inside a single Card. Title: "New account" / "Edit account". Card content: `<AccountForm>` + form-level button row. Card width inherits the layout's `max-w-3xl`.

### Field order (top-down)

```
┌─ New account ──────────────────────────────────────────────────────┐
│  Name *               [_______________________________________]    │
│  Type *               [Asset                            ▾]         │  Create only — Popover+Command
│                       [Asset 🔒]                                   │  Edit shows static, no picker
│  Currency *           [EUR — Euro €                     ▾]         │  Create only — Popover+Command
│                       [EUR — Euro € 🔒]                            │  Edit shows static, no picker
│  Description          [_______________________________________]    │
│                                                                     │
│  ─── Asset-specific (renders when Type = Asset) ───                │
│  ◯ Exclude from spendable balance                                  │  Switch
│    Excluded accounts don't count toward your spendable balance     │
│    but still appear in net worth.                                  │
│                                                                     │
│  ─── Liability-specific (renders when Type = Liability) ───        │
│  Repayment type       [— None —                          ▾]        │  Popover+Command
│    Full Monthly — pay the full balance each month; no interest.   │
│    Amortising — fixed monthly payment with interest (loans, …).   │
│  Interest rate        [_______________________________________]    │  Renders when Repayment = Amortising
│    Annual interest rate as a decimal. e.g. 0.035 = 3.5%.          │
│                                                                     │
│  ─── (Create only — opening balance always visible) ───            │
│  Opening balance      [____________________________________0.00]   │
│    Leave at 0 to start with no balance. A negative value records   │
│    a liability opening balance.                                    │
│  Start date           [03/05/2026                            📅]   │
│    The date this account starts being tracked. Defaults to today.  │
│                                                                     │
│  ─── (Edit only — opening balance behind disclosure) ───           │
│  ▶ Advanced — opening balance and start date                       │  Click to expand
│                                                                     │
│  [Save]   [Cancel]                                                 │
└────────────────────────────────────────────────────────────────────┘
```

### Field details

| Field | Required | Edit behaviour | Validation |
|---|---|---|---|
| Name | yes | editable | trim, 1–100 chars |
| Type (`AccountTypeId`) | yes | **server-immutable** — render static row with lock icon + tooltip | one of {1: Asset, 2: Liability} |
| Currency (`CurrencyId`) | yes | **server-immutable** — same static-row pattern, currency-specific tooltip wording | one of seeded 6 currencies (EUR, USD, GBP, COP, ARS, VED) |
| Description | no | editable | optional, trim, max 500 chars |
| Exclude from spendable | no (Asset only) | editable | bool — server forces `false` for Liability accounts (already enforced in `AccountService.TryUpdateAsync`) |
| Repayment type | no (Liability only) | editable | one of {null/None, "FullMonthly", "Amortising"} |
| Interest rate | conditional (Liability + Amortising) | editable | 0.0–1.0 (decimal — e.g. 0.035 for 3.5%) |
| Opening balance | yes (Create) / advanced (Edit) | editable | -999,999,999,999.99 to 999,999,999,999.99 |
| Start date | yes (Create) / advanced (Edit) | editable | DateOnly; defaults to today on Create |

### Tooltip wording for locked fields on Edit

- **Type lock:** "Account type cannot be changed after creation. Create a new account if you need a different type."
- **Currency lock:** "Account currency cannot be changed after creation. Create a new account if you need a different currency."

Both render via the same `<Tooltip>` + small lock-icon button pattern Categories uses (see `CategoryForm.tsx` for the precedent).

### Conditional rendering rules (decided in §11 Q8)

**On Create:**
- Type picker is interactive. Picking Asset → render Asset block (Exclude from spendable switch), hide Liability block.
- Picking Liability → render Liability block (Repayment type picker), hide Asset block.
- Inside the Liability block: picking Repayment type = Amortising → reveal Interest rate input.
- Switching Amortising → FullMonthly or None: hide Interest rate field, **but keep the cached value in form state**. If the user re-picks Amortising, the cached value reappears.

**On Edit:**
- Type and Currency render as static rows with lock icons (no pickers).
- Asset or Liability block renders based on the existing type — no toggle needed.
- Repayment type sub-picker stays interactive on Liability accounts (changing repayment strategy is allowed without changing the account type).

### Submit-time normalisation (Layer 1 of two-layer interest-rate defense)

The form submit handler always normalises the wire payload before sending:

```typescript
const body = {
  ...values,
  interestRate:
    values.repaymentType === 'Amortising'
      ? values.interestRate
      : null,
  liabilityRepaymentType:
    accountType === 'Asset'  // Use the locked type on Edit, the picked type on Create
      ? null
      : values.liabilityRepaymentType,
};
```

This means the form can hold cached state for UX continuity (C2 in Q8), but the wire is always self-consistent: rate is non-null iff Amortising; liabilityRepaymentType is non-null iff Liability. Layer 2 is the symmetric server policy (§8).

### Buttons

- **Save** — primary variant, default Button. Disabled until form is dirty (state differs from `snapshot` loaded on mount). Shows "Saving…" and stays disabled while the request is in-flight.
- **Cancel** — `variant="outline"`. Always enabled (except during submit). Calls `navigate('/accounts')`.
- **No Reset button.** Categories session decision; reasoning preserved in §11.

### Dirty tracking

- Form holds `snapshot` (loaded values on mount, set via `useState(initialValues)`) and `values` (current state).
- `isDirty = !shallowEqual(snapshot, values)`. Save disabled when `!isDirty`.
- After successful save: `setSnapshot(values)` so the form reads as clean if the user lingers (cosmetic — we navigate away on success, but the moment matters during the in-flight UX).

### Error handling on submit

- 2xx → `toast.success("Created.")` (Create) or `toast.success("Saved.")` (Edit) → `ctx.refetch()` → `navigate('/accounts')`.
- 422 (validation) → `toast.error("Couldn't save. Try again.")` → form retains values. Generic copy; per-field server-error surfacing is deferred (Categories baseline).
- Network error / other → same generic toast.

### Skeletons during initial load (Edit only)

Edit needs to fetch both the account detail (`GET /api/accounts/:id`) and the account-types list (`GET /api/account-types`) before rendering. While either is loading: render a Card with the title and 4 Skeleton rows in the body.

Create only fetches account-types. Same skeleton shell during that load.

### Currency picker on Create

The Currency picker uses Popover+Command. Display format in the trigger and option list: `{Code} — {Name} {Symbol}` — e.g. `EUR — Euro €`. The `value` for filter matching is the Code (so typing "eur" matches the Euro option).

There are 6 seeded currencies; no need for a search-as-you-type filter inside the popover (the list is small enough). The Command component's built-in fuzzy match handles it for free.

---

## 6. Row menu + Archive flow

### Row menu (`AccountRowMenu`)

| Row state | Menu items |
|---|---|
| Active | Edit · View ledger · Archive… |
| Archived | View ledger |

- **Edit** — `navigate('/accounts/:id/edit')`. Active rows only.
- **View ledger** — `navigate('/accounts/:id/ledger')`. Always present — auditing an archived account is a legitimate, frequent reason to open the ledger.
- **Archive…** — opens the AlertDialog. Active rows only. Ellipsis signals "this opens a confirm dialog" (Categories convention, lifted from native macOS).

There is **no Reactivate item.** The Accounts API has no reactivate endpoint; restoring an archived account is a future capability, out of scope here. Archived rows have only the View ledger item.

### Archive AlertDialog — adaptive copy

The list endpoint returns `HasTransactions: bool` per row (§8). `AccountRowMenu` reads it and branches the dialog content:

**Empty case (`HasTransactions === false`):**

```
┌─ Archive 'Cash'? ─────────────────────────────────────────────┐
│                                                                  │
│ This account has no transactions. It will be hidden from the    │
│ active list and pickers; you can find it again with the         │
│ Include archived toggle. Safe to archive.                        │
│                                                                  │
│                                       [Cancel]  [Archive]       │
└──────────────────────────────────────────────────────────────────┘
```

**Non-empty case (`HasTransactions === true`):**

```
┌─ Archive 'Checking Account'? ─────────────────────────────────┐
│                                                                  │
│ This account will be hidden from the active list and pickers.   │
│ Existing transactions stay attached to it, and the balance      │
│ still counts toward your net worth and reports. You can find    │
│ archived accounts with the toggle.                              │
│                                                                  │
│                                       [Cancel]  [Archive]       │
└──────────────────────────────────────────────────────────────────┘
```

### Title format

`Archive '{accountName}'?` — single quotes around the name, identical in both cases. Categories convention.

### Archive request

- `PATCH /api/accounts/:id/archive` — no body.
- 204 → `toast.success("Archived.")` → close dialog → `onChanged()` (refetches parent list) → row disappears from the active view (or reappears as archived with the muted treatment if Include archived is on).
- Other (non-2xx) → `toast.error("Couldn't archive. Try again.")`.

### Difference vs. Categories archive

There is **no 409 in-use error path** for accounts. The server's `TryDeactivateAsync` succeeds unconditionally (per data model: archived accounts still count toward calculations, so "has transactions" is not a blocker). The adaptive copy is a UX accommodation for the consequence; it is not a blocker.

### Component shape

Same shape as `CategoryRowMenu` (and ultimately `BudgetRowMenu`):

- `<DropdownMenu>` with `<DropdownMenuTrigger render={<Button variant="ghost" size="icon" aria-label="Row actions">...</Button>} />`.
- `<DropdownMenuContent align="end">` with the items above as `<DropdownMenuItem>` rows.
- `<AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>` rendered as a sibling, opened on click of the Archive… item.
- The handler chain on Archive click:
  1. `setConfirmOpen(false)` — close the dialog optimistically.
  2. `await fetch(CATEGORY_ARCHIVE_URL(...), { method: 'PATCH' })` — equivalent for accounts.
  3. Branch on response status; toast accordingly.
  4. On 2xx, call `onChanged()`.

---

## 7. Ledger page (`/app/accounts/:id/ledger`)

### Page width

`max-w-4xl` (896px). Wider than the list page (768px) because the ledger has 5 columns and the Description cell needs room for full transaction descriptions.

### Layout

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ ← Back to Accounts                                                            │
│                                                                                │
│ Checking Account — Ledger                                       [Archived]     │
│ Every entry that contributes to this account's balance, in chronological     │
│ order. The final running balance matches the account balance.                │
│                                                                                │
│ ┌──────────────── Payoff projection ────────────────────────────────────┐    │
│ │ Outstanding balance        €5,234.00                                   │    │
│ │ Annual interest rate              3.50%                                │    │
│ │                                                                          │    │
│ │ Monthly payment            [____________]   [Calculate]                │    │
│ │                                                                          │    │
│ │ Estimated payoff           March 2027 (10 months)                      │    │
│ │ Total interest cost              €87.42                                │    │
│ │ Total paid                  €5,321.42                                  │    │
│ └──────────────────────────────────────────────────────────────────────┘    │
│                                                                                │
│ ┌──────────────────────────────────────────────────────────────────────┐     │
│ │ Date         Description           Category       Amount    Running    │     │
│ │ ─────────────────────────────────────────────────────────────────     │     │
│ │ 03/04/2026   Opening Balance       —              +€5,000   €5,000    │     │
│ │ 04/04/2026   Salary March          Salary         +€2,500   €7,500    │     │
│ │ 05/04/2026   Groceries             Groceries        −€85    €7,415    │     │
│ │ 10/04/2026   Transfer to Savings   —                −€500   €6,915    │     │
│ └──────────────────────────────────────────────────────────────────────┘     │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Header

- "← Back to Accounts" link — sits above the h1, separate row, ghost-link styling. Calls `navigate('/accounts')`.
- `<h1>` `{accountName} — Ledger`. Inline `<Badge variant="secondary">Archived</Badge>` appended to the right when the account's `isActive === false` (so the user knows they're looking at a closed account).
- One-line description: "Every entry that contributes to this account's balance, in chronological order. The final running balance matches the account balance."
- Heading focused on mount (Categories pattern).

### Payoff projection card

**Conditional rendering:** renders only when ALL of:
- `account.accountTypeName === 'Liability'`
- `account.liabilityRepaymentType === 'Amortising'`
- `account.interestRate !== null`

If any of these is false, the projection card is omitted entirely (no placeholder, no "n/a" — just absent).

**Position:** above the ledger table, inside the page container.

**Layout:** dl-list at the top with two rows (Outstanding balance, Annual interest rate), then a monthly-payment input row with a Calculate button, then three result rows (Estimated payoff, Total interest cost, Total paid) that appear after a successful Calculate.

**Math runs SPA-side** (decided in §11 Q6 sub-decision). Closed-form amortisation formula:

```typescript
function project(balance: number, annualRate: number, monthlyPayment: number) {
  const monthlyRate = annualRate / 12;
  const monthsToPayoff = Math.ceil(
    -Math.log(1 - (balance * monthlyRate) / monthlyPayment) /
     Math.log(1 + monthlyRate)
  );
  const totalPaid = monthsToPayoff * monthlyPayment;
  const totalInterest = totalPaid - balance;
  // payoffDate = today + monthsToPayoff months
  return { monthsToPayoff, totalPaid, totalInterest, payoffDate };
}
```

Edge cases the formula must handle:
- `monthlyPayment <= balance * monthlyRate` → the payment doesn't cover the interest. Render an inline error ("Monthly payment is too low to ever pay off this balance — it must exceed the monthly interest cost.") instead of the result rows.
- `monthlyPayment <= 0` → render an error ("Monthly payment must be greater than zero.").
- `annualRate <= 0` → straight-line division (`monthsToPayoff = ceil(balance / monthlyPayment)`, no interest math).

**Reasoning for SPA-side over an API endpoint:** the math is a single closed-form formula, the Razor projection service dies in this cutover so there's no drift surface post-cleanup, a round-trip per Calculate is wasteful for sub-millisecond math, and if we later want the projection elsewhere (a dashboard tile) we lift the formula into a shared SPA util — same effort as building an API endpoint.

### Ledger table

5 columns:

| Column | Width | Alignment | Notes |
|---|---|---|---|
| Date | w-28 | left | Format using user's DateFormat from Settings |
| Description | flex-1 | left | Truncate with ellipsis on overflow + tooltip on hover for full text |
| Category | w-40 | left | Renders `—` for entries with no category (transfers, liability payments) |
| Amount | w-32 | right, `tabular-nums` | Sign-driven color (positive = green-ish via `text-success` or similar; negative = `text-destructive`). Sign prefix: `+` for positive, `−` for negative |
| Running balance | w-32 | right, `tabular-nums` | Account-type-driven color (Asset = `text-foreground`, Liability = `text-destructive`) — same convention as the list page |

**Two distinct color conventions on this page, intentional:**
- **Amount** (per-row flow) → sign-driven. Income reads green, expense reads red. Matches user mental model of "money in / money out."
- **Running balance** (state) → account-type-driven (B4 from Q7). A Liability ledger renders running balance red the whole way down (consistent with the list page); an Asset ledger renders running balance normal foreground unless it dips negative.

This split is deliberate — flow is naturally signed, balance is contextual.

**Sort:** chronological ascending (oldest first), so running balance grows top-down to its current value. Matches Razor.

**Pagination:** none. Account ledgers are typically <500 entries; lazy/virtualised rendering is a separate concern. Logged for follow-up if a user has 5,000+ entries.

### Empty state

"No entries found for this account." Italic muted text inside the table body. Shape matches Razor.

### Loading state

If the projection card is going to render, 4 dl skeleton rows in the projection area + 6–8 skeleton rows in the table area.

### Error states

- `GET /api/accounts/:id/ledger` returns 404 → "That account doesn't exist." banner + "← Back to Accounts" button (same shape Categories uses for missing entities).
- Other error → `<CardError section="ledger" onRetry={ledger.refetch} />`.

---

## 8. Server-side changes

Three areas of server work, in dependency order.

### 8a. `AccountListItemDto` gains `HasTransactions: bool`

Add the field to the record. Update the list endpoint to populate it:

```csharp
foreach (var a in accounts)
{
    var balance = await accountService.GetBalanceAsync(a.Id);
    var hasTransactions =
        await db.Transactions.Owned(user).AnyAsync(t => t.AccountId == a.Id) ||
        await db.Transfers.Owned(user).AnyAsync(t => t.SourceAccountId == a.Id || t.DestAccountId == a.Id) ||
        await db.LiabilityPayments.Owned(user).AnyAsync(p => p.AssetAccountId == a.Id || p.LiabilityAccountId == a.Id);
    dtos.Add(new AccountListItemDto(... existing fields ..., balance, hasTransactions));
}
```

`HasTransactions` is true if **any** movement touches the account — Transactions, Transfers, or LiabilityPayments. The dialog copy says "existing transactions stay attached," which a user reads as "any history" not specifically "Transaction rows." Excluding Transfers/LiabilityPayments would mean an account whose only history is a Transfer would get the "no transactions, safe to archive" copy — wrong.

**Performance note:** 3 extra queries per account. For 4–15 accounts, fine. Folded into a single grouped query later if it becomes a hotspot.

**Why on the list DTO and not the detail DTO:** the AlertDialog opens from a row in the list, so the flag must travel with the list payload. Fetching it on dialog-open would add a per-archive round-trip for no benefit.

### 8b. `AccountPolicies.ValidateLiabilityRepayment` extended to the None case

Current code rejects:
- Amortising + null rate (must have rate)
- FullMonthly + non-null rate (must not have rate)

Missing: null repaymentType + non-null rate. Add:

```csharp
if (repaymentType is null && interestRate is not null)
    return Result.Fail(InvalidLiabilityRepaymentCode,
                       "An account with no repayment type must not have an interest rate.");
```

This is **Layer 2** of the two-layer interest-rate normalisation. Layer 1 (SPA submit-time normalisation, §5) prevents the SPA from sending a stale rate; Layer 2 prevents any future client (mobile, scripts, integration tests) from bypassing.

**Tests added** alongside existing `AccountPoliciesTests`:
- `ValidateLiabilityRepayment_WithNoneAndRate_Fails` — covers the new branch.
- `ValidateLiabilityRepayment_WithNoneAndNoRate_Succeeds` — covers the symmetric pass case.

### 8c. Razor cutover

**`AccountsController` slims to 5 redirects.** The constructor's `IAccountService`, `AppDbContext`, and `ILiabilityProjectionService` dependencies are removed — redirects need none of them. Final shape:

```csharp
public class AccountsController : Controller
{
    public IActionResult Index()             => Redirect("/app/accounts");
    public IActionResult Create()            => Redirect("/app/accounts/new");
    public IActionResult Edit(Guid id)       => Redirect($"/app/accounts/{id}/edit");
    public IActionResult Deactivate(Guid id) => Redirect("/app/accounts");
    public IActionResult Ledger(Guid id)     => Redirect($"/app/accounts/{id}/ledger");
}
```

302, not 301 — same cache-friendliness reasoning Categories used. The redirects vanish in the final SPA-cleanup batch (per `docs/planning-phase3-spa-migration.md` §8).

**`IAccountService` and `AccountService` — drop the throwing CRUD methods.** Keep:
- `GetAllAsync(bool includeInactive)`
- `GetByIdAsync(Guid id)`
- `GetBalanceAsync(Guid id)`
- `GetOpeningBalanceAsync(Guid id)`
- `GetOpeningBalanceDateAsync(Guid id)`
- `GetLedgerAsync(Guid id)`
- `TryCreateAsync(CreateAccountRequest)`
- `TryUpdateAsync(Guid, UpdateAccountRequest)`
- `TryDeactivateAsync(Guid)`

Drop:
- `CreateAsync(AccountCreateViewModel vm)` — throwing version, only Razor uses.
- `UpdateAsync(AccountEditViewModel vm)` — throwing version, only Razor uses.

The plan-writing step audits for any other throwing variants by grepping the service file.

If `using ProjectCeres.ViewModels;` becomes unused on either file (because the kept methods only reference `CreateAccountRequest`/`UpdateAccountRequest` from the same namespace), keep it — no churn.

**Razor views deleted:** all 5 (`Index`, `Create`, `Edit`, `Deactivate`, `Ledger`).

**ViewModels deleted:** `AccountCreateViewModel`, `AccountEditViewModel`, `AccountLedgerViewModel`.

**Test audits:**
- `AccountServiceTests.cs` — drop tests that exercise deleted throwing methods. If file becomes empty, delete entirely (Categories precedent).
- `UserIdStampingTests.cs` — drop `RazorAccountService_*_stamps_UserId` test if it exists with the comment indicating it guards a Razor-only path (Categories precedent — `RazorCategoryService_CreateAsync_stamps_UserId` was deleted in commit `9578f9a` for the same reason).

**Projection service deletion:** `LiabilityProjectionService` (in `ProjectCeres/Services/`), `ILiabilityProjectionService`, and `LiabilityProjectionViewModel` (in `ProjectCeres/ViewModels/`) have only one consumer today: the Razor `AccountsController.Ledger` action. Once the Razor Ledger action is slimmed to a redirect (above), the projection service has zero callers. **Delete all three files**, plus the `AddScoped<ILiabilityProjectionService, LiabilityProjectionService>()` line in `Program.cs`. The amortisation formula now lives only in the SPA (`AccountLedger.tsx` or a small util), as decided in Q6 sub-decision (single source of truth post-cutover).

If `LiabilityProjectionServiceTests.cs` exists in the test project, **delete it too** — it tests the now-removed implementation.

**No new server endpoints.** Projection math is SPA-side; ledger endpoint already exists.

---

## 9. Tests, verification gate, commit shape

### Client tests (Vitest + React Testing Library, co-located)

| File | Coverage |
|---|---|
| `AccountsTable.test.tsx` | Renders one row per account · Type column shows Asset/Liability · Balance is right-aligned with `tabular-nums` · Liability rows render Balance in `text-destructive` · Asset rows render Balance in `text-foreground` · Asset overdraft (negative balance) renders normal foreground with minus sign · Liability negative balance renders red with minus sign · Archived rows have `opacity-60` + inline Archived badge · Active rows have `⋯` menu · Empty state ("No accounts.") |
| `AccountRowMenu.test.tsx` | Active rows show Edit + View ledger + Archive · Archived rows show only View ledger · Edit navigates to `/accounts/:id/edit` · View ledger navigates to `/accounts/:id/ledger` · Archive opens AlertDialog · Empty-account dialog uses safe copy · Non-empty-account dialog uses consequence copy · 204 fires `toast.success("Archived.")` and onChanged · Other status fires generic error toast |
| `AccountForm.test.tsx` | Renders Name/Type/Currency/Description with initial values (Edit) · Type picker editable on Create · Type read-only on Edit (no aria-expanded) · Currency picker editable on Create, read-only on Edit · Asset block renders when Type=Asset, hidden when Liability · Liability block renders when Type=Liability, hidden when Asset · Picking Amortising reveals Interest rate field · Switching Amortising→FullMonthly hides Interest rate but keeps cached value · Switching FullMonthly→Amortising shows the cached Interest rate · Submit normalises `interestRate: null` when repayment type isn't Amortising · Submit normalises `liabilityRepaymentType: null` when type is Asset · Save disabled when not dirty · Save enabled when Name changes · Save shows "Saving…" while pending · Save greys back out after `ok:true` · Inputs retain edits when `ok:false` · Cancel calls onCancel · Advanced disclosure starts collapsed (Edit) · Opening balance + start date hidden until expanded (Edit) · Hint text visible inside expanded section (Edit) |
| `AccountsLayout.test.tsx` | Renders skeleton while loading · Renders the four-column table with active accounts · `+ New account` link navigates to `/accounts/new` · Search filters rows (debounced) · Search-empty state with Clear search · Include archived toggle adds archived rows · Archived rows render Archived badge + opacity treatment · GET error renders CardError with Retry · First-run empty state renders when no accounts at all · "No accounts." message renders when only archived exist (and toggle is off) |
| `AccountCurrencySubtotals.test.tsx` | Hidden when ≤1 currency in active accounts · Renders one entry per currency when ≥2 · Net per currency = sum(asset balances) − sum(liability balances) · Format uses currency symbol + amount · Layout: inline list separated by middle dot · Excludes archived accounts from the math |
| `AccountCreate.test.tsx` | Renders form when account-types load · POST 2xx fires `toast.success("Created.")` and navigates back · POST 422 fires error toast and form retains values · Network error fires error toast |
| `AccountEdit.test.tsx` | Renders form pre-populated · GET 404 renders not-found banner with Back to Accounts link · PATCH success fires `toast.success("Saved.")` and navigates back · PATCH 422 fires error toast and form retains values · Type and Currency render as locked (no picker) on Edit · Advanced disclosure can be expanded to reveal opening-balance fields |
| `AccountLedger.test.tsx` | Renders header with account name · Archived badge appears for archived accounts · Description hint sentence renders · Renders one row per ledger entry · Amount column color (green/red) follows sign · Running-balance column color follows account-type convention (Asset normal, Liability red) · Empty state ("No entries found for this account.") · 404 renders not-found banner · Projection card renders only for Liability+Amortising+InterestRate accounts · Hidden for Asset accounts · Hidden for Liability+FullMonthly · Calculate computes payoff months from balance/rate/payment · Result rows appear after Calculate · Edge case: payment too low to cover interest → inline error · Edge case: payment ≤ 0 → inline error · Edge case: rate = 0 → straight-line division |

Estimated total: ~9 test files, ~60 tests.

### Server tests (xUnit + Moq + FluentAssertions)

| File | Additions / changes |
|---|---|
| `AccountPoliciesTests.cs` | Add `ValidateLiabilityRepayment_WithNoneAndRate_Fails` · Add `ValidateLiabilityRepayment_WithNoneAndNoRate_Succeeds` |
| `AccountsCrudApiTests.cs` (already exists) | Add `Get_returns_HasTransactions_true_for_account_with_transactions` · Add `Get_returns_HasTransactions_false_for_account_with_no_movements` · Add `Get_returns_HasTransactions_true_for_account_with_only_a_Transfer` · Add `Get_returns_HasTransactions_true_for_account_with_only_a_LiabilityPayment` |
| `AccountServiceTests.cs` | Drop tests calling deleted throwing CRUD methods. Empty file → delete entirely |
| `UserIdStampingTests.cs` | Drop `RazorAccountService_*_stamps_UserId` if it exists |

### Verification gate (before commit)

1. **`dotnet test`** — all server tests green.
2. **`cd ProjectCeres.Client && pnpm test`** — all client tests green.
3. **`cd ProjectCeres.Client && pnpm build`** — production build green.
4. **Documentation sync** — invoke the `sync-docs` skill against the working diff. Per saved feedback (`feedback_sync_docs_before_spa_commits.md`), this is a named gate, not "if I remember." High-value targets for this commit:
   - `docs/planning-phase3-spa-migration.md` §2 controller table — flip the `AccountsController` row to "Migrated 2026-05-03" with commit hash placeholder, spec link, plan link.
   - `docs/planning-phase3-spa-migration.md` §8 "Frontend execution batches" table — flip Accounts row from "Pending" to "✅ Migrated 2026-05-03 (commit `<sha>`)".
   - `docs/planning-phase3.md` §14 — add the "✓ **Accounts — list + Create/Edit + Ledger + payoff projection** (2026-05-03)" bullet under group 7 with spec/plan citations.
   - `docs/api-contract.md` — audit; add `HasTransactions: bool` to `AccountListItemDto` documentation if the doc enumerates DTOs exhaustively.
   - `docs/models.md` — verify the Account section is still accurate (the `Notes (Phase 3)` mention stays; `Notes` is still planned, just deferred from this scope).
   - No ADR needed.
   - **Verify each proposed update against the actual file state before applying** — do not trust diff inference alone (saved feedback `feedback_changelog_verify_before_proposing.md`).
5. **Manual click-through** (user step):
   - Visit `/app/accounts`. Default view shows 4 active accounts + per-currency subtotal hidden (single currency).
   - Search "che". Filters to "Checking Account" if substring matches. Clear search.
   - Toggle Include archived. Archived appear with opacity + badge.
   - Click `⋯ → Edit` on Checking Account. Form pre-populates. Type and Currency locked. Description editable. Click Cancel → back to list.
   - Click `⋯ → Archive…` on a fresh test account with no transactions. Empty-case copy. Click Archive. Toast appears, row disappears.
   - Click `⋯ → Archive…` on Checking Account (has transactions). Non-empty-case copy. Click Cancel.
   - Click `+ New account`. Pick Type=Liability, Currency=EUR, Repayment type=Amortising, Interest rate=0.035, Name=Test Loan, Opening balance=5000. Save. Toast appears, returns to list, new account renders red.
   - Click `⋯ → View ledger` on Test Loan. Projection card renders (Liability + Amortising + rate). Enter Monthly payment=500. Click Calculate. Result rows populate.
   - Visit `/Accounts`. 302 → `/app/accounts`. Same for `/Accounts/Create`, `/Accounts/Edit/<guid>`, `/Accounts/Deactivate/<guid>`, `/Accounts/Ledger/<guid>`.
6. **Commit** — single commit, all changes (code + tests + docs) atomic.

### Commit shape

**Single commit**, same as Categories (`9578f9a`). All work — client + server policy + API DTO + Razor cutover + tests + doc sync — lands together. The Razor controller can't be slimmed until the SPA pages exist; the SPA pages can't ship without the API DTO change; the test files reference both sides; the docs claim "Accounts is migrated" only after the code makes it true. Splitting wouldn't pass `dotnet build` mid-PR and would leave the docs lying for an interim window.

**Commit message scaffold:**

```
feat(spa): Accounts page + ledger + Razor cutover

Replaces the /app/accounts placeholder with a full Accounts SPA:
list page (search, Include archived toggle, per-currency subtotal,
adaptive archive AlertDialog), Create/Edit forms (Type+Currency
locked on Edit, conditional Asset/Liability fields, opening balance
behind Advanced disclosure on Edit), and a dedicated Ledger page
(running balance + payoff projection for amortising liability
accounts). Slims the Razor AccountsController to five 302 redirects.

[... details, locked decisions, server changes, tests, doc sync ...]

Spec: docs/superpowers/specs/2026-05-03-accounts-spa-design.md
Plan: docs/superpowers/plans/2026-05-03-accounts-spa.md
```

---

## 10. Out of scope (logged elsewhere)

These are real follow-ups, not "deferred forever." Each has a destination and an explicit trigger.

| Item | Destination | Trigger |
|---|---|---|
| Movements "Active accounts only" filter chip | `docs/planning-future.md` → "Maybe / Future Consideration" | When archived-account noise becomes real friction in Movements |
| Dashboard net-worth tile "Includes archived accounts" tooltip | `docs/planning-future.md` → "Maybe / Future Consideration" | When user feedback indicates confusion about net worth not dropping after archive |
| Receivables (assets where someone owes the user, with optional interest accrual) | `docs/planning-future.md` → "Maybe / Future Consideration" | Phase 4 (freelancer/autónomo support); needs its own brainstorm to settle account-modelling fork, accrual mechanism, collection-event modelling |
| `Notes` column on Account | Separate small plan, post-SPA migration | When `Notes` is needed as a distinct field from `Description` (currently the SPA uses `Description`) |
| Account reactivate flow | Not yet logged | When the deletion-strategy ADR is updated to define reactivate semantics for accounts |
| Ledger pagination / virtualisation | Not yet logged | When a user has 5,000+ ledger entries on a single account |
| Per-field server-error surfacing on form submit (vs. generic toast) | App-wide concern, not Accounts-specific | When the generic toast becomes insufficient across multiple SPA pages |

---

## 11. Decision log

12 brainstorm questions answered + 1 follow-up logged. Source: brainstorm session 2026-05-03.

| Q | Topic | Decision |
|---|---|---|
| 1 | Ledger scope | Dedicated `/accounts/:id/ledger` page with running balance + payoff projection panel for amortising liabilities |
| 2 | Notes column | Defer; SPA uses existing Description |
| 3 | Routing | Categories shape; Ledger as a sibling top-level route, not nested in the Accounts layout `<Outlet>` |
| 4 | Archive policy + AlertDialog copy | Adaptive copy via `HasTransactions` flag on `AccountListItemDto`. Movements + Dashboard untouched (follow-ups logged) |
| 5 | List columns | Four columns: Name · Type · Balance · `⋯`. Currency implicit in symbol; Status implicit in row treatment |
| 6 | Tabs vs single list | Single mixed list, no tabs, no Type filter chip |
| 7 | Balance presentation | A1 right-aligned + B4 color by account type + C1 per-currency subtotal (≥2 currencies only). Net per currency = sum(asset) − sum(liability) |
| 8 | Conditional form fields | A1 inline conditional on Create + B1 type-locked hint on Edit + C2 cache interest rate when switching repayment types. Layer 1 (SPA submit normalisation) + Layer 2 (server policy tightened to reject `null + non-null`) both in scope |
| 9 | Opening balance editability | Behind "Show advanced" disclosure on Edit; inline on Create. Warning text inside expanded section |
| 10 | Empty state | Three states: first-run CTA + archived-only-empty + search-empty (Clear search link) |
| 11 | System-row treatment | None. Every account is fully user-editable. No leading lock icon, no pinned-to-bottom sort, no special styling |
| 12 | Filter affordances | Search input (debounced 200ms, client-side) + Include archived toggle. Categories pattern minus tabs |
| follow-up | Receivables | Logged to `planning-future.md` as a Phase 4 candidate with architectural sketch + open forks |

### Reasoning for the load-bearing calls

- **Q4 — adaptive AlertDialog copy:** the consequence of archiving an empty account ("safe to archive") differs materially from archiving an account with history ("balance still counts toward net worth"). Showing the consequence text universally would be misleadingly heavy for empty accounts; showing the safe text universally would be misleadingly light for accounts with history. Cost is one extra DB query per row in the list endpoint — negligible at this dataset size.
- **Q7 — color by account type, not sign:** the credit-card-with-refund-credit case (where a liability balance can be negative) breaks any sign-driven colour scheme. Type-driven colour is the only convention that holds for both account types in legitimate scenarios. The brainstorm explicitly verified that `Account.GetBalanceAsync` does not clamp signs, so both positive and negative balances can occur for both types.
- **Q8 — two-layer interest-rate normalisation:** Layer 1 (SPA submit normalisation) gives the user good ergonomics (cached value when toggling repayment types); Layer 2 (server policy) gives any future client (mobile, scripts) the same correctness guarantee. The brainstorm uncovered that today the server has no guard for the None case, which would silently allow stale rates to persist. Layer 2 is a 3-line server change; including it in this scope is cheap insurance.
- **Q11 — no system accounts:** the user explicitly chose flexibility over guard-rails. Some apps lock down system accounts (Cash, Accounts Payable) and that pattern is frustrating to customise. The brainstorm decision: if a user needs a "system-style" account, they can create it themselves and treat it as such by convention. Every account is renameable, archivable, and editable.

### Items the brainstorm explicitly considered and rejected

- Asset/Liability tabs (Q6) — too much chrome for a small bisected dataset; users typically want a combined view ("what's my net worth?").
- Search-omission for short lists (Q12) — defensible but inconsistent across the SPA; consistency wins, search costs near zero.
- Per-row "this account has N transactions" preview before confirming archive — overengineering; the adaptive empty-vs-non-empty copy handles the meaningful split without adding numeric noise.
- API endpoint for payoff projection math (Q6 sub-decision) — wasteful round-trip for sub-millisecond closed-form math; the Razor projection service dies in this cutover anyway.
- Notes column added as a third free-text field alongside Description (Q2 option C) — speculative; defer until a real use case demands it.

---

**End of spec.**
