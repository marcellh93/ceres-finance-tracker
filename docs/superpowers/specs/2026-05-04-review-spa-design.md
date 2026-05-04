# Review SPA — Design Spec

> **Status:** Spec locked 2026-05-04. Committed alongside implementation.

---

## 1. Context

**Why this is happening.** Phase 3 SPA migration Batch 2 #6 (Review). The Razor `TransferReviewController` and `ReconciliationReviewController` (and their two `Index.cshtml` views) get retired in favour of a unified React SPA at `/app/review`. Both API controllers (`ReconciliationReviewApiController`, `TransferReviewApiController`) already shipped — this is mostly a frontend exercise plus three small, well-bounded server changes.

**What's new vs Recurring/Reports patterns.**

- **Two queues, one page** — Reconciliations (auto-cleared rows the user can confirm or dispute) and Transfers (rows that look like transfer halves, awaiting Link / Create / Dismiss). Unified under `/app/review` with shadcn Tabs.
- **A3 default-tab logic** — page lands on whichever tab has work; if both have work or both are empty, default to Reconciliations. URL is *not* rewritten on first paint to avoid back-button traps.
- **`ReviewCountProvider`** — sibling of the existing `ReminderCountProvider`. Wraps the app shell; exposes `{ reconciliationCount, transferCount, total, refresh }` to the Sidebar (combined badge) and `ReviewLayout` (per-tab badges). Refreshes after every mutation in either tab.
- **Hybrid action model on Reconciliations** — primary `Confirm match` button inline (the benign common case), `Dispute` lives behind a `⋯` row menu and opens an AlertDialog. Reflects the asymmetric blast radius (Confirm = no side effects; Dispute = un-clears original + inserts new transaction marked Needs Review).
- **`TransferActionDialog` with `mode` prop** — Link and Create share a dialog shape (account picker, submit/cancel, success/error handling). Dismiss fires immediately (no dialog) — least destructive of the three.
- **Picker pre-filtering** — the account picker in the Transfers dialogs filters out (1) the staged row's own account, (2) inactive accounts, (3) different-currency accounts. The same-currency rule is enforced server-side too (`TryCreateAsTransferAsync`); pre-filtering is the SPA's job to keep the UI honest.
- **Server-side cleanup** — drops the throwing CRUD variants from `ITransferReviewService` and `IImportStagedTransactionService` (Categories/Accounts/Recurring/Reports precedent). Adds `TryConfirmAllAsync` so the API has a uniformly Result-returning interface. Enriches both staged DTOs with `AccountCurrencyCode` + `AccountCurrencySymbol`.

## 2. Goals & non-goals

### Goals

- Replace the placeholder `/app/review` route with a real Review SPA: a tabbed page (`Reconciliations` · `Transfers`) covering every action the Razor pages cover today.
- **A3 default tab logic:** route lands on whichever tab has pending items; if both have items or both are empty, default to Reconciliations.
- **B1 URL state:** `?tab=transfers` query param survives refresh and supports deep-linking; absent param = default tab.
- **C1 empty-tab UX:** both tab triggers always visible with their count badge; an empty tab renders an inline empty state when selected.
- Apply the locked SPA-page template established by Settings, Categories, Accounts, Recurring, and Reports. **Approach 3 — flat `features/review/` folder.**
- **A1 sidebar count:** sidebar nav item `Review` shows a single combined count badge (`Review · 3`) when total > 0; nothing when 0.
- **C3 ReviewCountProvider:** wrap the app shell with a provider that exposes `{ transferCount, reconciliationCount, total, refresh }`, fanning out to the two existing `/pending/count` endpoints.
- **Reconciliations tab interactions (Q3):** **A3 hybrid** — primary `Confirm` button inline, `Dispute` lives behind the `⋯` row menu; **B1** Dispute opens an AlertDialog with consequence copy; **C2** `Confirm all` button at the top of the tab opens an AlertDialog with explicit count.
- **Transfers tab interactions (Q4):** **A2** three buttons per card (`Link to existing`, `Create transfer`, `Dismiss`); Link/Create open AlertDialogs containing the account picker; **B1** Dismiss fires immediately; **C1** Link button is hidden when no candidate exists.
- **No trailing ellipsis** on any button, menu item, AlertDialog action, or label.
- **Server changes:**
  - Drop throwing variants from `ITransferReviewService` and `IImportStagedTransactionService` (Categories/Accounts/Recurring precedent).
  - Add `Task<Result> TryConfirmAllAsync()` to `IImportStagedTransactionService`; API controller migrates to it.
  - Enrich `StagedTransactionDto` and `StagedTransferDto` with `AccountCurrencyCode` + `AccountCurrencySymbol`.
  - Slim `TransferReviewController` and `ReconciliationReviewController` to redirects.
- Bell stays Recurring-only — Review surfaces are sidebar count + tab counts.

### Non-goals

- **No undo affordance on the Review page itself.** Reversal exists in Movements (delete the inserted transaction, re-mark cleared). Logged in §11.
- **No `View transaction →` toast actions** for Dismiss / Link / Create. Would require switching the API contract from 204 to 201 with a transaction-id body. Logged in §11.
- **No bell aggregation across Recurring + Review.** The bell stays single-purpose.
- **No new server count-aggregation endpoint.** The provider fans out to two existing `/pending/count` endpoints.
- **No changes to the import detection / staging logic** in `ImportService`. This SPA only reads from and acts on already-staged rows.
- **No changes to Movements.** The result of a Review action looks identical to a manually-entered Transaction or Transfer.
- **No reactivate retro-fit for Categories or Accounts.** Already logged elsewhere.
- **No new ADR.** Existing ADR-0046 covers the import staging surface.

---

## 3. File structure

### Create — client

```
ProjectCeres.Client/src/app/features/review/
  review-api.ts                           ← URL builders + DTOs (logic-free)
  ReviewLayout.tsx                        ← Tabs shell, ?tab= sync, A3 default routing
  ReviewLayout.test.tsx
  ReviewCountProvider.tsx                 ← Context: { transferCount, reconciliationCount, total, refresh }
  ReviewCountProvider.test.tsx

  ReconciliationList.tsx                  ← cards + Confirm-all button + empty/loading/error states
  ReconciliationList.test.tsx
  ReconciliationCard.tsx                  ← single staged-transaction card; inline Confirm + ⋯ menu
  ReconciliationCard.test.tsx
  ReconciliationDisputeDialog.tsx         ← AlertDialog body for Dispute
  ReconciliationDisputeDialog.test.tsx
  ReconciliationConfirmAllDialog.tsx      ← AlertDialog body for Confirm-all
  ReconciliationConfirmAllDialog.test.tsx

  TransferList.tsx                        ← cards + empty/loading/error states
  TransferList.test.tsx
  TransferCard.tsx                        ← single staged-transfer card; three buttons (conditional Link)
  TransferCard.test.tsx
  TransferActionDialog.tsx                ← AlertDialog with account picker, mode='link' | 'create'
  TransferActionDialog.test.tsx
```

`TransferActionDialog` is one component with a `mode` prop. The differences between `mode="link"` and `mode="create"` are limited to: dialog title, body copy, submit endpoint, success-toast text, submit-button label. Picker, validation, error handling shared.

### Modify — client

- `ProjectCeres.Client/src/app/pages/Review.tsx` — replace placeholder with one-line re-export `export { ReviewLayout as Review } from '../features/review/ReviewLayout';`.
- `ProjectCeres.Client/src/app/App.tsx` — wrap the app shell in `<ReviewCountProvider>` (sibling of `<ReminderCountProvider>`). Route `<Route path="review" element={<Review />} />` stays as a flat route — no nested children needed (tabs use a query param, not a sub-route).
- `ProjectCeres.Client/src/app/layout/Sidebar.tsx` — `Review` nav item gains a count badge driven by `useReviewCount().total` (hidden when 0).
- `ProjectCeres.Client/src/app/layout/Sidebar.test.tsx` — assert the badge renders/hides correctly and the aria-label includes the count when present.
- `ProjectCeres.Client/src/app/App.test.tsx` — flip the placeholder Review test (`expects h1 'Review'`) to expect the live page.

### Modify — server

- `ProjectCeres/Controllers/TransferReviewController.cs` — slim to redirects (4 actions → 4 redirects, all 302).
- `ProjectCeres/Controllers/ReconciliationReviewController.cs` — slim to redirects (4 actions → 4 redirects, all 302).
- `ProjectCeres/Controllers/Api/ReconciliationReviewApiController.cs` — `ConfirmAll` action migrates to `TryConfirmAllAsync` so the controller is uniformly Result-returning.
- `ProjectCeres/Controllers/Api/ReconciliationReviewApiController.cs` — `GetPending` projection adds `AccountCurrencyCode` + `AccountCurrencySymbol` to the DTO.
- `ProjectCeres/Controllers/Api/TransferReviewApiController.cs` — `GetPending` projection adds `AccountCurrencyCode` + `AccountCurrencySymbol` to the DTO.
- `ProjectCeres/Services/ITransferReviewService.cs` — drop the three throwing methods (`LinkToExistingAsync`, `CreateAsTransferAsync`, `DismissAsTransactionAsync`). Keep the four GET / Try-* methods.
- `ProjectCeres/Services/TransferReviewService.cs` — drop the throwing method bodies. Verify the `pending` query eager-loads `Account.Currency` (add `.ThenInclude(a => a.Currency)` if missing).
- `ProjectCeres/Services/IImportStagedTransactionService.cs` — drop the three throwing methods (`ConfirmAsync`, `ConfirmAllAsync`, `DisputeAsync`); add `Task<Result> TryConfirmAllAsync()`.
- `ProjectCeres/Services/ImportStagedTransactionService.cs` — drop the throwing method bodies; add `TryConfirmAllAsync` (filters to current user's `Pending` rows; sets `Status = Confirmed`, `ResolvedAt = UtcNow`; always returns `Result.Ok()` — no per-row failure modes). Verify the `pending` query eager-loads `Account.Currency` (add include if missing).
- `ProjectCeres/ViewModels/ReconciliationReviewApiDtos.cs` — add `AccountCurrencyCode` and `AccountCurrencySymbol` to `StagedTransactionDto`.
- `ProjectCeres/ViewModels/TransferReviewApiDtos.cs` — add `AccountCurrencyCode` and `AccountCurrencySymbol` to `StagedTransferDto`.
- `ProjectCeres.Tests/Integration/ImportStagedTransactionServiceTests.cs` — drop tests against deleted throwing methods. Add `TryConfirmAllAsync` tests (see §8).
- `ProjectCeres.Tests/Integration/TransferReviewServiceTests.cs` — drop tests against deleted throwing methods. (`Try*` variants are already covered.)
- `ProjectCeres.Tests/Integration/Api/ReconciliationReviewApiTests.cs` — verify `POST /api/reconciliation-review/confirm-all` still returns 204 after migration to `TryConfirmAllAsync`. Add 2 currency-enrichment tests.
- `ProjectCeres.Tests/Integration/Api/TransferReviewApiTests.cs` — add 2 currency-enrichment tests.
- `ProjectCeres.Tests/Integration/Api/UserIdStampingTests.cs` — drop any `RazorTransferReviewService_*_stamps_UserId` / `RazorImportStagedTransactionService_*_stamps_UserId` tests if they exist (Categories/Accounts/Recurring precedent).
- The Razor `_Layout.cshtml` sidebar — drop the pending-transfer and pending-reconciliation count badges. They linked to controllers that are now redirect-only; in the Razor shell that no SPA user sees, the badges add no value pre-Batch-4 cleanup.

### Delete — server

- `ProjectCeres/Views/TransferReview/Index.cshtml`
- `ProjectCeres/Views/ReconciliationReview/Index.cshtml`
- `ProjectCeres/ViewModels/StagedTransferViewModel.cs` (Razor-only)
- `ProjectCeres/ViewModels/StagedTransactionViewModel.cs` (Razor-only)

The `StagedTransferDto` and `StagedTransactionDto` records in `TransferReviewApiDtos.cs` and `ReconciliationReviewApiDtos.cs` stay — the SPA uses them.

---

## 4. Routing, navigation, URL state

### Route structure (`App.tsx`)

```tsx
<Route path="review" element={<Review />} />
```

Flat. No nested children — both tabs share the same route, switched by `?tab=`. AlertDialogs (Dispute, Confirm-all, Transfer link/create) have no route; they spawn from buttons or row menus.

### Tab state — query param `?tab=`

| URL | Active tab |
|---|---|
| `/app/review` (no param) | A3 default — see logic below |
| `/app/review?tab=reconciliations` | Reconciliations |
| `/app/review?tab=transfers` | Transfers |
| `/app/review?tab=anything-else` | Treated as no param → falls through to A3 default |

**A3 default tab logic** (runs on mount when `tab` param absent):

```typescript
function pickDefaultTab(reconciliationCount: number, transferCount: number): TabKey {
  // "show whichever has work; if both have work or both are empty, Reconciliations"
  if (reconciliationCount > 0) return 'reconciliations';
  if (transferCount > 0)       return 'transfers';
  return 'reconciliations';
}
```

**When the default resolves, the URL is *not* rewritten.** A bare `/app/review` stays bare in the address bar — the tab switch is component-state only on the first render, then any user-initiated tab change writes the param. Reasoning: rewriting the URL on mount creates a back-button trap (Back goes to the same page with a different param, instead of leaving the page).

**Switching tabs** — clicking the other tab trigger sets `?tab=<key>` via `setSearchParams({ tab: key }, { replace: true })`. `replace: true` so tab toggling doesn't pile up history entries.

### Page width

`max-w-4xl` (896px) centered, matching Recurring. Cards stack vertically.

### Navigation entry points

| Destination | Triggered from |
|---|---|
| `/app/review` | Sidebar nav item "Review"; legacy Razor Import Summary tile (via the redirect). |
| `/app/review?tab=reconciliations` | Tab click. Razor `ReconciliationReviewController` redirect targets. |
| `/app/review?tab=transfers` | Tab click. Razor `TransferReviewController` redirect targets. |

The Razor `Views/Import/Summary.cshtml` "Reconciled" tile still points at the legacy `/ReconciliationReview` URL, which 302s to `/app/review?tab=reconciliations`. When the Import SPA lands (Batch 2 #7), the SPA tile can deep-link directly.

### Razor redirects — per-area cutover

`TransferReviewController` becomes:

```csharp
public class TransferReviewController : Controller
{
    public IActionResult Index() => Redirect("/app/review?tab=transfers");
    public IActionResult LinkToExisting(Guid stagedId, Guid otherAccountId) => Redirect("/app/review?tab=transfers");
    public IActionResult CreateAsTransfer(Guid stagedId, Guid otherAccountId) => Redirect("/app/review?tab=transfers");
    public IActionResult DismissAsTransaction(Guid stagedId) => Redirect("/app/review?tab=transfers");
}
```

`ReconciliationReviewController` becomes:

```csharp
public class ReconciliationReviewController : Controller
{
    public IActionResult Index() => Redirect("/app/review?tab=reconciliations");
    public IActionResult Confirm(Guid id) => Redirect("/app/review?tab=reconciliations");
    public IActionResult ConfirmAll() => Redirect("/app/review?tab=reconciliations");
    public IActionResult Dispute(Guid id) => Redirect("/app/review?tab=reconciliations");
}
```

302 (default), not 301 — same cache reasoning as Categories/Accounts/Recurring/Reports.

### Sidebar entry

```
Sidebar (existing items unchanged)
  Dashboard
  Movements
  Accounts
  Categories
  Budgets
  Recurring transactions    [bell-driven count]
  Reports
  Review              [3]   ← new badge, hidden when total === 0
  Settings
```

The badge uses the same visual idiom as the topbar bell badge — small pill with the number, hidden when zero. Driven by `useReviewCount().total`. Aria-label includes the count when present (`aria-label="Review, 3 pending"`), nothing extra when zero.

---

## 5. `ReviewLayout` shell

### Layout

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ Review                                                                        │  ← h1, max-w-4xl
│ Triage rows the importer staged for you. Confirm what's right, dispute       │
│ what's wrong, link transfers to their other side.                            │
├──────────────────────────────────────────────────────────────────────────────┤
│ ┌──────────────────────────────────────────────────────────────────────────┐ │
│ │  [ Reconciliations  2 ]  [ Transfers  1 ]                                 │ │
│ │  ─────────────────────                                                     │ │
│ │                                                                            │ │
│ │  ⟨ Active tab content rendered here ⟩                                     │ │
│ │                                                                            │ │
│ └──────────────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Header

- `<h1>` "Review" (text-2xl font-semibold). `tabIndex={-1}`, focused on mount via headingRef.
- One-line muted description below the h1: "Triage rows the importer staged for you. Confirm what's right, dispute what's wrong, link transfers to their other side."

### Tabs

Use the shadcn `Tabs` primitive (already in the component library — listed in `planning-phase3.md` §13 component additions).

```tsx
<Tabs value={activeTab} onValueChange={handleTabChange}>
  <TabsList>
    <TabsTrigger value="reconciliations">
      Reconciliations
      {reconciliationCount > 0 && <CountBadge>{reconciliationCount}</CountBadge>}
    </TabsTrigger>
    <TabsTrigger value="transfers">
      Transfers
      {transferCount > 0 && <CountBadge>{transferCount}</CountBadge>}
    </TabsTrigger>
  </TabsList>
  <TabsContent value="reconciliations">
    <ReconciliationList />
  </TabsContent>
  <TabsContent value="transfers">
    <TransferList />
  </TabsContent>
</Tabs>
```

Both tab triggers always visible. The badge renders only when `count > 0`, sitting inline to the right of the label with `gap-2`.

### A3 default tab resolution

On mount with no `?tab=` param:

```tsx
useEffect(() => {
  if (searchParams.get('tab')) return;        // explicit param wins, no auto-switch
  if (loading) return;                        // wait for counts
  if (userTouchedTab.current) return;         // user already chose, don't override
  if (reconciliationCount === 0 && transferCount > 0 && activeTab !== 'transfers') {
    setActiveTab('transfers');
  }
}, [loading, reconciliationCount, transferCount]);
```

The very first paint may show "Reconciliations" briefly even when the right answer is "Transfers" — but only for ~100ms while the count fetch resolves. Worth it for the simpler logic and to avoid a "tab flickered out from under me" feel if the count call is slow.

### Tab content rendering

- Each tab's component (`ReconciliationList`, `TransferList`) owns its own data fetching.
- Content only mounts when its tab is active (`<TabsContent>` is conditional in shadcn's primitive).
- Switching to Transfers triggers a fresh `GET /api/transfer-review/pending` rather than pre-loading both.
- Toggling tabs back and forth re-fetches. No client cache layer (Recurring re-fetches the same way).

### Loading, error, empty for the *layout itself*

- Layout shell never fails to load — there's no API call at the layout level (counts come from the provider).
- If the count provider is still loading on mount: tab triggers render without count badges (just the labels).
- If the count provider errored: tab triggers render without badges; per-tab content surfaces its own error if its fetch fails.

### `ReviewCountProvider` semantics

```tsx
type Ctx = {
  reconciliationCount: number;
  transferCount: number;
  total: number;                    // derived: sum of the two
  loading: boolean;                 // true while either count is in-flight
  refresh: () => void;              // refetches both
};
```

- Wraps the app shell in `App.tsx`, sibling of `<ReminderCountProvider>`.
- Issues two `useApi` calls: `/api/reconciliation-review/pending/count` and `/api/transfer-review/pending/count`.
- `refresh()` refetches both. Called by every list-page mutation.
- `loading === true` while at least one of the two is in-flight; once both resolve, `loading` flips to `false` regardless of whether either errored.
- On individual count error: the count for that queue is treated as `0` for badge purposes (sidebar / tab badge hides). Logged to console; no toast.
- `<Sidebar>` consumes `useReviewCount().total` for its badge. `<ReviewLayout>` consumes the per-queue counts for tab badges and the A3 default.

---

## 6. Reconciliations tab

### Layout

Inside `<TabsContent value="reconciliations">`:

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  These rows were automatically matched to existing transactions during      │  ← muted
│  import. Confirm correct matches or dispute incorrect ones.                 │     description
│                                                                              │
│                                              [✓ Confirm all]                │  ← right-aligned, only
│                                                                              │     when count > 0
│  ┌─ Card ──────────────────────────────────────────────────────────────┐   │
│  │ Checking — imported 03/05/2026 09:14                                 │   │
│  │ 02/05/2026   −500.00                                                  │   │
│  │ Transfer to savings                                                   │   │
│  │ ┃ Matched to: 02/05/2026  −500.00  Wire to savings account            │   │
│  │                                                                        │   │
│  │                                          [✓ Confirm match]   [⋯]      │   │
│  └────────────────────────────────────────────────────────────────────┘   │
│                                                                              │
│  ┌─ Card ──────────────────────────────────────────────────────────────┐   │
│  │ ...                                                                   │   │
│  └────────────────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Tab description

Muted text (text-sm text-muted-foreground), one line:

> "These rows were automatically matched to existing transactions during import. Confirm correct matches or dispute incorrect ones."

### Confirm-all button

- Visible only when `staged.length > 0`. Hidden on empty/loading/error states.
- Right-aligned in a flex row with the description on the left (or below the description on narrow widths via flex-wrap).
- `variant="outline"`, leading check icon, label "Confirm all".
- Opens `<ReconciliationConfirmAllDialog>`.
- During flight: `disabled` + "Confirming…" label.

### Card layout (`ReconciliationCard`)

One Card per `StagedTransactionDto`. Inside:

| Slot | Content | Style |
|---|---|---|
| Top-left meta | `{accountName} — imported {ImportedAt:dd/MM/yyyy HH:mm}` | text-xs text-muted-foreground |
| Raw row line | `{rawDate} {rawAmount}` (amount colored: rose for negative, emerald for positive) — `tabular-nums` | text-base font-semibold |
| Raw description | `{rawDescription}` (only when non-null) | text-sm text-muted-foreground |
| Match line | Inline pill: `Matched to: {matchedDate} {matchedAmount} {matchedDescription}` | text-sm with sky-tinted background pill, mt-2 |
| Action row | Right-aligned `Confirm match` primary button + `⋯` row menu trigger | mt-3 flex justify-end gap-2 |

Date format respects user `Settings.DateFormat`; amount uses `Settings.NumberFormat` and the new `accountCurrencySymbol` field on the DTO.

### Row menu (`⋯`)

Single item: `Dispute`. Opens `<ReconciliationDisputeDialog>`. Pattern matches Recurring's row menu — shadcn `DropdownMenu` with `align="end"`.

### Inline `Confirm match` button

- Primary variant, leading check icon, text "Confirm match".
- Click: `POST /api/reconciliation-review/{id}/confirm` directly (no AlertDialog — Q3 A3 + B1).
- 204 → `toast.success("Confirmed.")` → `ctx.refetch()` (list) → `provider.refresh()` (counts).
- 404 → `toast.error("That row no longer exists. Refreshing.")` → `ctx.refetch()`.
- Other → `toast.error("Couldn't confirm. Try again.")`.
- During flight: button shows "Confirming…" and is disabled.

### Dispute dialog (`ReconciliationDisputeDialog`)

```
┌─ Dispute this match? ──────────────────────────────────────────────┐
│ The matched transaction will be marked as not cleared, and this   │
│ CSV row will be inserted as a new transaction marked              │
│ "Needs review."                                                    │
│                                                                     │
│ You can fix mistakes from the Movements page.                     │
│                                                                     │
│                                       [Cancel]  [Dispute match]   │
└────────────────────────────────────────────────────────────────────┘
```

- Submit: `POST /api/reconciliation-review/{id}/dispute`.
- 204 → `toast.success("Disputed. Original un-cleared and a new transaction added.")` → `ctx.refetch()` → `provider.refresh()` → dialog closes.
- 404 → `toast.error("That row no longer exists. Refreshing.")` → dialog closes → `ctx.refetch()`.
- 422 → `toast.error("Couldn't dispute. Try again.")` → dialog stays open.
- Cancel button (`variant="outline"`) closes without action. Esc-key closes.

### Confirm-all dialog (`ReconciliationConfirmAllDialog`)

```
┌─ Confirm all 5 matches? ───────────────────────────────────────────┐
│ This accepts every staged match in the list. You can still        │
│ adjust individual transactions later from Movements, but          │
│ confirming clears them from this queue all at once.               │
│                                                                     │
│                                          [Cancel]  [Confirm all]  │
└────────────────────────────────────────────────────────────────────┘
```

- Title interpolates the count captured at the moment the dialog was opened (frozen, not live — the title doesn't flip mid-dialog if rows arrive in the background). Both the dialog title and the success toast use this same captured count.
- Submit: `POST /api/reconciliation-review/confirm-all`.
- 204 → `toast.success("Confirmed N matches.")` (N from the captured count) → `ctx.refetch()` → `provider.refresh()` → dialog closes.
- Other → `toast.error("Couldn't confirm all. Try again.")` → dialog stays open.
- The endpoint is naturally idempotent (only Pending rows are touched). If the queue was emptied between dialog-open and submit, the response is still 204; the toast will say "Confirmed N matches" using the captured count. Acceptable cosmetic divergence; not worth a special case.

### Empty state

```
┌─────────────────────────────────────────┐
│                                         │
│         No reconciliations to review.   │
│                                         │
│   The importer hasn't auto-matched any  │
│   rows that need your confirmation.     │
│                                         │
└─────────────────────────────────────────┘
```

- Heading (text-base font-medium): "No reconciliations to review."
- Body (text-sm muted): "The importer hasn't auto-matched any rows that need your confirmation."
- No CTA button.

### Loading state

Five `<Skeleton>` cards inside the tab area (~80px tall). Confirm-all area shows nothing during initial load.

### Error state

`<CardError section="Reconciliations" onRetry={list.refetch} />`.

---

## 7. Transfers tab

### Layout

Inside `<TabsContent value="transfers">`:

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  These rows look like transfers between accounts. Resolve each one          │  ← muted
│  before they appear as plain transactions.                                  │     description
│                                                                              │
│  ┌─ Card ──────────────────────────────────────────────────────────────┐   │
│  │ Checking — imported 03/05/2026 09:14                                 │   │
│  │ 02/05/2026   −500.00                                                  │   │
│  │ Transfer to savings                                                   │   │
│  │ ┃ Possible match: 02/05/2026  +500.00  Incoming transfer              │   │
│  │                                                                        │   │
│  │       [Link to existing]   [Create transfer]   [Dismiss]              │   │
│  └────────────────────────────────────────────────────────────────────┘   │
│                                                                              │
│  ┌─ Card (no candidate) ───────────────────────────────────────────────┐   │
│  │ Savings — imported 03/05/2026 09:14                                  │   │
│  │ 02/05/2026   +200.00                                                  │   │
│  │ Random deposit                                                        │   │
│  │                                                                        │   │
│  │                          [Create transfer]   [Dismiss]                │   │
│  └────────────────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Tab description

> "These rows look like transfers between accounts. Resolve each one before they appear as plain transactions."

### Card layout (`TransferCard`)

| Slot | Content | Style |
|---|---|---|
| Top-left meta | `{accountName} — imported {ImportedAt:dd/MM/yyyy HH:mm}` | text-xs text-muted-foreground |
| Raw row line | `{rawDate} {rawAmount}` (amount colored) — `tabular-nums` | text-base font-semibold |
| Raw description | `{rawDescription}` (only when non-null) | text-sm text-muted-foreground |
| Candidate pill | Inline pill: `Possible match: {candidateDate} {candidateAmount} {candidateDescription}` (only when `candidateTransactionId !== null`) | text-sm with sky-tinted background pill, mt-2 |
| Action row | Right-aligned: `Link to existing` (only when candidate exists), `Create transfer`, `Dismiss` | mt-3 flex justify-end gap-2 |

### Action buttons

- **`Link to existing`** — primary variant, leading link icon. Visible only when `candidateTransactionId !== null`. Opens `<TransferActionDialog mode="link">`.
- **`Create transfer`** — outline variant, leading plus icon. Always visible. Opens `<TransferActionDialog mode="create">`.
- **`Dismiss`** — `variant="outline"` with `text-destructive`. Always visible. Fires immediately, no dialog.

Visual hierarchy: Link is primary when present (the suggested resolution); Create is the fallback the user reaches for if Link is wrong or absent; Dismiss is the "this isn't a transfer at all" escape hatch.

### Dismiss action (no dialog)

- Click: fires `POST /api/transfer-review/{id}/dismiss-as-transaction` directly.
- During flight: button shows "Dismissing…" and is disabled.
- 204 → `toast.success("Imported as a plain transaction.")` → `ctx.refetch()` → `provider.refresh()`.
- 404 → `toast.error("That row no longer exists. Refreshing.")` → `ctx.refetch()`.
- Other → `toast.error("Couldn't dismiss. Try again.")`.

### Transfer action dialog (`TransferActionDialog`)

Single component with `mode: 'link' | 'create'` prop.

```
┌─ Link to existing transfer ────────────────────────────────────────┐
│ Pick the account on the other side of this transfer. We'll link   │
│ this row to the matching transaction we found there.              │
│                                                                     │
│ Other account *  [— Select account —              ▾]              │
│                                                                     │
│                                       [Cancel]  [Link transfer]   │
└────────────────────────────────────────────────────────────────────┘
```

vs

```
┌─ Create transfer ──────────────────────────────────────────────────┐
│ Pick the account on the other side of this transfer. We'll        │
│ create a new transfer record between the two accounts.            │
│                                                                     │
│ Other account *  [— Select account —              ▾]              │
│                                                                     │
│                                       [Cancel]  [Create transfer] │
└────────────────────────────────────────────────────────────────────┘
```

| Aspect | `mode="link"` | `mode="create"` |
|---|---|---|
| Title | "Link to existing transfer" | "Create transfer" |
| Body copy | "Pick the account on the other side of this transfer. We'll link this row to the matching transaction we found there." | "Pick the account on the other side of this transfer. We'll create a new transfer record between the two accounts." |
| Submit endpoint | `POST /api/transfer-review/{id}/link-to-existing` | `POST /api/transfer-review/{id}/create-as-transfer` |
| Submit body | `{ otherAccountId }` | `{ otherAccountId }` |
| Success toast | "Linked." | "Transfer created." |
| Submit button label | "Link transfer" | "Create transfer" |

### Picker (`Other account`)

The locked Popover+Command idiom. Filters to:
- `account.id !== card.accountId` (no self-transfer)
- `account.isActive === true` (no inactive accounts)
- `account.currencyCode === card.accountCurrencyCode` (same-currency rule)

Server enforces the same-currency rule in `TryCreateAsTransferAsync` (returns 422 with a domain error on mismatch); the SPA pre-filters so the user never picks an invalid option.

If the filtered list is empty, the picker still renders but with an inline empty-state inside the popover: "No eligible accounts. Transfers must be between accounts of the same currency." Submit button disabled.

### Submit handling

- **Picker empty** (no selection): submit button disabled.
- **Picker selected** + submit: button shows "Linking…" / "Creating…", disabled during flight.
- 204 → success toast → `ctx.refetch()` → `provider.refresh()` → dialog closes.
- 404 → `toast.error("That row no longer exists. Refreshing.")` → dialog closes → `ctx.refetch()`.
- 422 with currency-mismatch domain code → `toast.error("That account doesn't share the staged row's currency.")` (defensive — picker should have prevented this).
- 422 with other domain code → `toast.error("Couldn't link. Try again.")` / `"Couldn't create transfer. Try again."` → dialog stays open.

Cancel (`variant="outline"`) closes without action. Esc-key closes.

### Empty state

```
┌─────────────────────────────────────────┐
│                                         │
│   No transfers to review.               │
│                                         │
│   The importer hasn't flagged any       │
│   rows that look like transfers.        │
│                                         │
└─────────────────────────────────────────┘
```

Same shape as Reconciliations empty state. No CTA.

### Loading state

Five `<Skeleton>` cards inside the tab area.

### Error state

`<CardError section="Transfers" onRetry={list.refetch} />`.

---

## 8. Server-side changes

Three concrete server changes plus the Razor cutover, in dependency order.

### 8a. Drop throwing variants from `ITransferReviewService` and `IImportStagedTransactionService`

Categories/Accounts/Recurring precedent. Once the Razor controller becomes redirect-only, the throwing methods have no callers and can go.

**`ITransferReviewService` after the cleanup:**

```csharp
public interface ITransferReviewService
{
    Task<IReadOnlyList<ImportStagedTransfer>> GetPendingAsync();
    Task<int> GetPendingCountAsync();
    Task<Result> TryLinkToExistingAsync(Guid stagedId, Guid otherAccountId);
    Task<Result> TryCreateAsTransferAsync(Guid stagedId, Guid otherAccountId);
    Task<Result> TryDismissAsTransactionAsync(Guid stagedId);
}
```

Removed: `LinkToExistingAsync`, `CreateAsTransferAsync`, `DismissAsTransactionAsync` (throwing).

**`IImportStagedTransactionService` after the cleanup:**

```csharp
public interface IImportStagedTransactionService
{
    Task<IReadOnlyList<ImportStagedTransaction>> GetPendingAsync();
    Task<int> GetPendingCountAsync();
    Task<Result> TryConfirmAsync(Guid id);
    Task<Result> TryConfirmAllAsync();          // ← NEW
    Task<Result> TryDisputeAsync(Guid id);
}
```

Removed: `ConfirmAsync`, `ConfirmAllAsync`, `DisputeAsync` (throwing).

### 8b. Add `Task<Result> TryConfirmAllAsync()`

```csharp
public async Task<Result> TryConfirmAllAsync()
{
    var pending = await db.ImportStagedTransactions
        .Where(t => t.Status == StagedTransactionStatus.Pending && t.UserId == currentUser.Get().UserId)
        .ToListAsync();

    var now = DateTime.UtcNow;
    foreach (var row in pending)
    {
        row.Status = StagedTransactionStatus.Confirmed;
        row.ResolvedAt = now;
    }
    await db.SaveChangesAsync();
    return Result.Ok();
}
```

The Result wrapping is for interface consistency — no per-row failure modes. API controller migrates to use it:

```csharp
[HttpPost("confirm-all")]
public async Task<IActionResult> ConfirmAll()
{
    var result = await stagedService.TryConfirmAllAsync();
    return result.IsSuccess ? NoContent() : ToErrorResponse(result.Error!.Value);
}
```

### 8c. Enrich `StagedTransactionDto` and `StagedTransferDto` with account currency

The SPA needs the account's currency code and symbol to (1) format amounts correctly, and (2) filter the Transfers picker to same-currency accounts.

**`StagedTransactionDto`:**

```csharp
public record StagedTransactionDto(
    Guid     Id,
    DateTime ImportedAt,
    Guid     AccountId,
    string   AccountName,
    string   AccountCurrencyCode,        // ← NEW
    string   AccountCurrencySymbol,      // ← NEW
    DateOnly RawDate,
    decimal  RawAmount,
    string?  RawDescription,
    Guid?    MatchedTransactionId,
    string?  MatchedTransactionDescription,
    DateOnly MatchedTransactionDate,
    decimal  MatchedTransactionAmount);
```

**`StagedTransferDto`:**

```csharp
public record StagedTransferDto(
    Guid     Id,
    DateTime ImportedAt,
    Guid     AccountId,
    string   AccountName,
    string   AccountCurrencyCode,        // ← NEW
    string   AccountCurrencySymbol,      // ← NEW
    DateOnly RawDate,
    decimal  RawAmount,
    string?  RawDescription,
    Guid?    CandidateTransactionId,
    string?  CandidateTransactionDescription,
    DateOnly? CandidateTransactionDate,
    decimal? CandidateTransactionAmount);
```

API controller mapping adjusts to project the two new fields. Verify the existing GET service shape eager-loads `Account.Currency` (add `.ThenInclude(a => a.Currency)` if missing).

### 8d. Razor cutover

Per §4: `TransferReviewController` and `ReconciliationReviewController` slim to redirects (4 actions each, all 302). Razor sidebar count badges for both queues are removed in the same change.

### 8e. What is NOT changing

- `ImportService` detection and staging logic — completely untouched.
- `ImportStagedTransaction` and `ImportStagedTransfer` entities and their migrations — untouched.
- `StagedTransactionStatus` and `StagedTransferStatus` enums — untouched.
- The action endpoints (`/confirm`, `/dispute`, `/link-to-existing`, `/create-as-transfer`, `/dismiss-as-transaction`) — same routes, same shapes, same status codes.
- No new domain logic; no new validation rules.

---

## 9. Tests, verification gate, commit shape

### Client tests (Vitest + React Testing Library, co-located)

| File | Coverage |
|---|---|
| `ReviewLayout.test.tsx` | Renders h1 + description · both tab triggers always visible · count badges render when count > 0, hidden when 0 · `?tab=transfers` selects Transfers on mount · no-param + reconciliations > 0 → Reconciliations active · no-param + reconciliations === 0 + transfers > 0 → Transfers active · no-param + both empty → Reconciliations active · clicking Transfers writes `?tab=transfers` (replace, not push) · user-touched tab is not auto-overridden when counts later change · **first-paint flicker — Reconciliations while loading then switches to Transfers when counts arrive 0/N** · **first-paint + user-touched-tab — post-load adjustment is suppressed when user already clicked** · **tab badges refresh when ReviewCountProvider.refresh() is called** · **Transfers fetch only fires after Transfers tab is selected (lazy mount)** |
| `ReviewCountProvider.test.tsx` | Provides counts from both endpoints · `total` is the sum · `refresh()` refetches both · single-endpoint error treats that count as 0; other count still works · `loading` is true while either is in flight · **refresh propagates to consumers (sidebar + tab badges in lockstep after a mutation)** |
| `ReconciliationList.test.tsx` | Skeletons during loading · cards render (one per DTO) · Confirm-all button visible when count > 0, hidden when 0 · empty state when queue empty · error state on fetch failure with retry |
| `ReconciliationCard.test.tsx` | Renders meta / amount / description / matched pill · amount color (negative red, positive green) · inline `Confirm match` button posts to `/api/reconciliation-review/{id}/confirm` · 204 fires success toast + onChanged + refresh · 404 fires "no longer exists" toast and refetches · other errors fire generic toast · row menu opens, shows `Dispute` only |
| `ReconciliationDisputeDialog.test.tsx` | Cancel closes without firing · submit posts to `/dispute` · success toast text matches contract · 404 closes dialog with refetch · 422 keeps dialog open with generic error |
| `ReconciliationConfirmAllDialog.test.tsx` | Title shows live count · submit posts to `/confirm-all` · success toast includes the count · errors keep dialog open |
| `TransferList.test.tsx` | Skeletons during loading · cards render (one per DTO) · empty state when queue empty · error state on fetch failure with retry |
| `TransferCard.test.tsx` | Renders meta / amount / description · candidate pill renders only when `candidateTransactionId !== null` · `Link to existing` hidden when no candidate · `Link to existing` opens dialog with `mode="link"` · `Create transfer` opens dialog with `mode="create"` · `Dismiss` fires immediately · Dismiss success toast + refetch + refresh · Dismiss 404 fires "no longer exists" toast and refetches · Dismiss other error fires generic toast · **Dismiss shows "Dismissing…" and disables button during flight** · **rapid double-click on Dismiss fires only one POST** |
| `TransferActionDialog.test.tsx` | `mode="link"` renders correct title / button label / endpoint · `mode="create"` renders correct title / button label / endpoint · picker filters out the card's own account · picker filters out inactive accounts · picker filters out different-currency accounts · picker empty-state when no eligible accounts · submit button disabled until selection made · 204 success toast + refetch + refresh + dialog closes · 404 closes dialog with refetch · 422 keeps dialog open with generic error · **422 with currency-mismatch domain code shows currency-specific toast** · Cancel closes without firing |
| `Sidebar.test.tsx` (additions) | Review badge renders when total > 0 · Review badge hidden when total === 0 · aria-label includes the count when present |

Estimated total: **~10 test files, ~80 tests.** Bolded items are added during the second-pass coverage audit.

### Server tests (xUnit + Moq + FluentAssertions)

| File | Additions / changes |
|---|---|
| `TransferReviewServiceTests.cs` | Drop tests against the three deleted throwing methods. |
| `ImportStagedTransactionServiceTests.cs` | Drop tests against `ConfirmAsync`/`ConfirmAllAsync`/`DisputeAsync`. Add: `TryConfirmAllAsync_confirms_all_pending_rows` · `TryConfirmAllAsync_is_idempotent_on_empty_queue` · `TryConfirmAllAsync_sets_ResolvedAt_on_every_confirmed_row` · **`TryConfirmAllAsync_does_not_touch_already_resolved_rows`** · **`TryConfirmAllAsync_does_not_confirm_other_users_pending_rows`** |
| `ReconciliationReviewApiTests.cs` | Verify `POST /api/reconciliation-review/confirm-all` still returns 204 after migration to `TryConfirmAllAsync`. Add: `Pending_includes_account_currency_code` · `Pending_includes_account_currency_symbol`. |
| `TransferReviewApiTests.cs` | Add: `Pending_includes_account_currency_code` · `Pending_includes_account_currency_symbol`. |
| `UserIdStampingTests.cs` | Drop `RazorTransferReviewService_*` and `RazorImportStagedTransactionService_*` tests if present. |
| `UiVerificationTests.cs` | Drop entries (if present) covering `/TransferReview` and `/ReconciliationReview` Razor view smoke tests. |

Bolded items are added during the second-pass coverage audit (multi-tenancy + idempotency).

### Verification gate (before commit)

1. **`dotnet test`** — all server tests green.
2. **`cd ProjectCeres.Client && pnpm test`** — all client tests green (foreground; per saved feedback `feedback_dont_background_one_shot_verifications.md`).
3. **`cd ProjectCeres.Client && pnpm build`** — production build green (foreground).
4. **Documentation sync** — invoke the `sync-docs` skill against the working diff. Per saved feedback `feedback_sync_docs_before_spa_commits.md`, this is a named gate. Targets:
   - `docs/planning-phase3-spa-migration.md` §2 controller table — flip both `TransferReviewController` and `ReconciliationReviewController` rows to "Migrated 2026-05-04" with commit hash placeholder, spec link, plan link.
   - `docs/planning-phase3-spa-migration.md` §8 frontend execution batches table — flip the Review row from "Pending" to "✅ Migrated 2026-05-04 (commit `<sha>`)".
   - `docs/planning-phase3.md` §14 — add the "✓ **Review — unified `/app/review` with Reconciliations + Transfers tabs, sidebar count badge, ReviewCountProvider** (2026-05-04)" bullet.
   - `docs/api-contract.md` — note the `StagedTransactionDto` and `StagedTransferDto` enrichment (`AccountCurrencyCode`, `AccountCurrencySymbol`); note the throwing-variant removal from the two services.
   - `docs/models.md` — no change needed (no entity or relationship changes).
   - **No new ADR.**
   - **Per-file verification rule:** verify each proposed update against actual file state before applying — saved feedback `feedback_changelog_verify_before_proposing.md`.
5. **Cross-module audit** — per saved feedback `feedback_audit_cross_module_queries_on_cutover.md`, even though Review isn't an entity cutover, grep the codebase one final time for any remaining callers of the dropped throwing methods. The Razor controller is the only known one; this is defense in depth.
6. **Manual click-through** (user step):
   - Visit `/app/review`. Page renders with both tabs. Default tab matches the A3 logic (whichever has work; fallback Reconciliations).
   - Sidebar `Review` shows the combined count badge if total > 0; hidden if 0.
   - Click `Reconciliations` tab. URL becomes `/app/review?tab=reconciliations`. Cards render. Each card shows account / date / amount / description / matched pill.
   - Click inline `Confirm match` on a row. Toast "Confirmed." Row disappears. Sidebar count + tab count drop by 1.
   - Click `⋯` on a different row → `Dispute`. AlertDialog opens with consequence copy. Click `Dispute match`. Toast "Disputed. Original un-cleared and a new transaction added." Row disappears. Counts drop.
   - Visit Movements. Verify the original transaction is no longer cleared. Verify a new transaction with `Needs review` flag exists for the disputed CSV row.
   - Click `Confirm all`. AlertDialog shows the live count. Click confirm. Toast "Confirmed N matches." All rows disappear. Tab shows empty state.
   - Click `Transfers` tab. URL becomes `/app/review?tab=transfers`. Cards render. Cards with a candidate show the pill + `Link to existing` button; cards without a candidate omit both.
   - Click `Link to existing` on a card with a candidate. Dialog opens with title "Link to existing transfer." Picker shows only same-currency, active, non-self accounts. Pick one. Click `Link transfer`. Toast "Linked." Row disappears. Counts drop. Visit Movements; verify a Transfer record exists.
   - Click `Create transfer` on another card. Dialog opens with title "Create transfer." Pick an account. Click `Create transfer`. Toast "Transfer created." Row disappears.
   - Click `Dismiss` on a card. No dialog — fires immediately. Toast "Imported as a plain transaction." Row disappears. Visit Movements; verify the row landed as a plain Transaction.
   - Trigger a same-currency-only constraint case: stage a transfer on an EUR account, click `Link to existing`, verify USD accounts are absent from the picker.
   - Visit `/TransferReview`. 302 → `/app/review?tab=transfers`. Same for `/ReconciliationReview` → `/app/review?tab=reconciliations`. Same for the action POST URLs.
   - Visit `/app/review?tab=garbage`. Falls through to A3 default.
   - Toggle tabs while one queue is empty: empty tab still appears with no badge, content shows the empty state.
   - Mobile width (375px): cards stack, action buttons wrap onto a second row inside the card without overflow. Tab triggers fit. Sidebar collapses as expected (existing behavior).
7. **Commit** — single commit, all changes (client + server + tests + docs) atomic.

### Commit shape

**Single commit**, same as Categories/Accounts/Recurring/Reports.

**Commit message scaffold:**

```
feat(spa): Review page with Reconciliations + Transfers tabs

Replaces the /app/review placeholder with a real Review SPA: a unified
page with two tabs (Reconciliations · Transfers) covering every action
the Razor pages cover today.

Reconciliations tab: inline `Confirm match` button on each card for the
common case; `Dispute` lives in a row menu and opens an AlertDialog with
consequence copy (un-clears original, inserts new transaction marked
Needs review). Top-of-tab `Confirm all` button gated by an AlertDialog
showing the live count.

Transfers tab: three buttons per card — `Link to existing` (hidden when
no candidate found), `Create transfer`, `Dismiss`. Link and Create open
an AlertDialog containing an account picker filtered to same-currency,
active, non-self accounts; Dismiss fires immediately.

Sidebar `Review` nav item gains a combined count badge driven by a new
ReviewCountProvider that fans out to /api/reconciliation-review/pending/count
and /api/transfer-review/pending/count.

Tab state is URL-synced via ?tab=, default tab follows whichever queue
has work (or Reconciliations if both empty / both have work).

Server changes:
- StagedTransactionDto and StagedTransferDto enriched with
  AccountCurrencyCode + AccountCurrencySymbol so the SPA can format
  amounts and filter the Transfers picker by currency.
- ITransferReviewService and IImportStagedTransactionService drop their
  throwing CRUD variants (Razor cutover precedent from Categories /
  Accounts / Recurring).
- IImportStagedTransactionService gains TryConfirmAllAsync; the API
  controller migrates to it for a uniformly Result-returning interface.

Slims the Razor TransferReviewController and ReconciliationReviewController
to redirects (4 + 4 actions, all 302). Deletes 2 Razor views and 2 Razor-
only ViewModels. Drops the Razor sidebar count badges for both queues.

Spec: docs/superpowers/specs/2026-05-04-review-spa-design.md
Plan: docs/superpowers/plans/2026-05-04-review-spa.md
```

---

## 10. Decision log

11 brainstorm questions and a second-pass coverage audit.

| Q | Topic | Decision |
|---|---|---|
| 1 | Page shape | Single unified `/app/review` page with shadcn Tabs (`Reconciliations` · `Transfers`) |
| 2a | Default tab on first visit | A3 — whichever tab has pending items; if both have items or both are empty, Reconciliations |
| 2b | URL state for active tab | B1 — `?tab=` query param, supports refresh + deep-linking |
| 2c | Empty-tab treatment | C1 — both tabs always visible; selected empty tab renders inline empty state |
| 3a | Reconciliations action model | A3 — primary `Confirm match` inline button + `Dispute` in `⋯` row menu |
| 3b | Dispute confirmation | B1 — AlertDialog with consequence copy |
| 3c | Confirm-all | C2 — keep, but gate behind AlertDialog with explicit count |
| 4a | Transfers picker placement | A2 — three buttons per card; picker lives in dialog |
| 4b | Dismiss confirmation | B1 — no dialog, fire immediately |
| 4c | No-candidate Link button | C1 — hide entirely when no candidate found |
| 5a | Sidebar count | A1 — single combined count badge, hidden when 0 |
| 5b | Topbar bell aggregation | B1 — bell stays Recurring-only |
| 5c | Count source | C3 — `ReviewCountProvider` over the two existing `/pending/count` endpoints |
| 6a | Throwing-variant cleanup | A1 — drop in this commit (Categories/Accounts/Recurring precedent) |
| 6b | `TryConfirmAllAsync` | B1 — add for interface consistency, even though it has no failure modes |
| 6c | Page-level undo | C1 — none in this commit; reversal exists in Movements; logged in §11 |
| Approach | React file structure | Approach 3 — flat `features/review/` folder, prefix-grouped components |
| Audit | Test coverage | +8 client tests (count propagation across consumers, tab-badge refresh, lazy Transfers mount, first-paint flicker, first-paint user-touched override, double-click prevention, Dismiss flight state, currency-mismatch toast) + 2 server tests (TryConfirmAllAsync ignores already-resolved rows, TryConfirmAllAsync respects multi-tenancy) |

### Reasoning for the load-bearing calls

- **Q1 / Approach 3 — unified page, flat folder:** The two queues are conceptually one thing ("things waiting on me from imports"). Splitting them into separate routes adds nav weight without making either page clearer. Inside the folder, every Batch 2 feature uses a flat structure; introducing nested subfolders for one feature would be inconsistent for marginal grouping value (the `Reconciliation*` and `Transfer*` filename prefixes already do that work).
- **Q2a — A3 default:** The user came here to act on something. Putting them on the queue with work avoids a click that returns no information. Rewriting the URL on first paint creates a back-button trap, so the address bar stays bare until the user explicitly switches tabs.
- **Q3a/b — hybrid action model:** Confirm and Dispute have asymmetric blast radii. Confirm = no side effects beyond status update. Dispute = un-clears original transaction + inserts a new transaction. Friction should match consequence: inline button for Confirm, row-menu + AlertDialog for Dispute.
- **Q4a — picker in dialog:** Inline pickers on every card scale poorly (a queue of 20 rows = 40 picker triggers on screen at once). Dialog hides the picker until the user has signaled intent, keeping cards calm.
- **Q5b — bell stays Recurring-only:** The bell already has a clear meaning ("things on a schedule that need confirming or skipping"). Mixing in import-staging triage muddies it. Sidebar count + tab counts cover Review.
- **Q6a — drop throwing variants:** Established Batch 2 cutover pattern. Keeping throwing methods alive for "minimal churn" creates dead code by the next commit anyway.
- **Q6c — no page-level undo:** The saved-feedback "Why" is "reversal exists in the same release" — and it does, in Movements. Page-level undo is real UX work for actions that, unlike Archive, leave evidence in the normal Movements list. Documented in §11.

### Items the brainstorm explicitly considered and rejected

- Two separate routes, `/app/review/transfers` + `/app/review/reconciliations` (Approach 2) — splits the unified-page concept at the router boundary, even though they share Sidebar entry, count provider, and Tab shell. Wrong cut.
- Nested subfolders inside `features/review/` (Approach 1) — first-of-its-kind departure from the locked SPA template; filename prefixes do the same grouping work without new structure.
- Hidden tab when count is 0 (Q2c C2) — layout shift on count change; "where did transfers go?" confusion.
- Disabled tab when count is 0 (Q2c C3) — feels punitive when the answer is just "nothing here."
- Inline pickers on every Transfers card (Q4a A1) — Razor parity but heavy at scale.
- Single "Resolve" button per card → branching dialog (Q4a A3) — collapses three semantically distinct actions; hides the option set.
- Bell aggregating Recurring + Review (Q5b B2) — muddies the bell's meaning.
- Two separate bells (Q5b B3) — doubles the topbar surface for low-frequency action.
- Server count-aggregation endpoint (Q5c C2) — premature; two existing endpoints are fine.
- Page-level Undo toast on Confirm (Q6c C2) — real UX work for actions where reversal exists elsewhere.
- `View transaction →` toast actions on Dismiss/Link/Create — would require switching API from 204 to 201 with body. Defer; logged in §11.

---

## 11. Out of scope (logged elsewhere)

| Item | Destination | Trigger |
|---|---|---|
| Page-level undo affordance for Review actions | `docs/planning-future.md` → "Maybe / Future Consideration" | If beta testers report difficulty finding what they just disputed/dismissed |
| `View transaction →` deep-link in toasts (Dismiss / Link / Create) | `docs/planning-future.md` | If the manual-Movements lookup proves friction-heavy in beta |
| Recurring + Review aggregation in the topbar bell | `docs/planning-future.md` | If sidebar+tab counts prove insufficient surface |
| Server count-aggregation endpoint (`GET /api/review/pending/count` returning `{ transfers, reconciliations }`) | `docs/planning-future.md` | If two parallel `useApi` GETs become a measurable perf concern |
| Razor `_Layout.cshtml` cleanup of all remaining queue-count badges | Batch 4 (`docs/planning-phase3-spa-migration.md`) | When the Razor shell is removed entirely |
| Reactivate retro-fit for Categories and Accounts | Separate small commit (already logged) | After this commit ships |
| Toast `aria-live` severity verification (cross-cutting) | `planning-phase3.md` §8 (accessibility baseline; vitest-axe + eslint-plugin-jsx-a11y) | Cross-cutting, not Review-specific |

---

**End of spec.**
