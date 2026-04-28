# Table UX Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix badge wrapping, restore interactive cleared toggle on Liability Payment rows, widen the page container so tables stop wrapping on desktop, and apply consistent visual polish across all data tables.

**Architecture:** Pure CSS + Razor view edits. No new services, controllers, migrations, or API changes. The React `ClearedBadge` component (`ProjectCeres.Client/src/components/ClearedBadge.tsx`) already supports `data-movement-type="transaction"` for liability payments — only the Razor view needs updating. Tailwind CSS is compiled at build time via `dotnet build` (triggers `tailwindcss` CLI). All changes are in `ProjectCeres/` — no changes to `ProjectCeres.Client/`.

**Tech Stack:** Tailwind CSS v3 (`ProjectCeres/Styles/app.css`), ASP.NET Core MVC Razor (`.cshtml` views), existing React `ClearedBadge` component.

---

## Files Modified

| File | What changes |
|---|---|
| `ProjectCeres/Styles/app.css` | `.page-content` max-width: `max-w-6xl` → `max-w-screen-xl` |
| `ProjectCeres/Views/Movements/Index.cshtml` | Badge nowrap; LiabilityPayment status → React component; filter inputs `w-auto`; Amount `text-right` |
| `ProjectCeres/Views/Transactions/Index.cshtml` | LiabilityPayment type → orange badge; Amount `text-right` |
| `ProjectCeres/Views/Transfers/Index.cshtml` | Amount `<th>` + `<td>` `text-right` |
| `ProjectCeres/Views/Accounts/Index.cshtml` | Balance colored by sign |
| `ProjectCeres/Views/Categories/Index.cshtml` | Status text → green/gray pill badges |

---

### Task 1: Widen page container (app.css)

**Files:**
- Modify: `ProjectCeres/Styles/app.css:114`

Context: `.page-content` is defined in `@layer components` around line 114. It currently uses `max-w-6xl` (72rem). We change it to `max-w-screen-xl` (80rem) so all data-dense tables have more horizontal room.

- [ ] **Step 1: Edit app.css**

In `ProjectCeres/Styles/app.css`, change line 114:

```css
/* BEFORE */
.page-content   { @apply max-w-6xl mx-auto px-6 py-8; }

/* AFTER */
.page-content   { @apply max-w-screen-xl mx-auto px-6 py-8; }
```

- [ ] **Step 2: Build to verify Tailwind compiles**

```bash
dotnet build ProjectCeres
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Styles/app.css ProjectCeres/wwwroot/css/site.css
git commit -m "fix: widen page-content from max-w-6xl to max-w-screen-xl to prevent table wrapping"
```

---

### Task 2: Fix Movements/Index.cshtml — badges, status, filter, amount

**Files:**
- Modify: `ProjectCeres/Views/Movements/Index.cshtml`

Four things to fix in this file:
1. Type badges need `whitespace-nowrap` so "Liability Payment" doesn't wrap
2. LiabilityPayment status cell: replace plain Razor spans with the React `ClearedBadge` component
3. Filter bar inputs need `w-auto` so they don't stretch full-width
4. Amount column `<th>` and all three Amount `<td>` variants need `text-right`

- [ ] **Step 1: Add whitespace-nowrap to all three type badges**

The three badge spans are at lines 50, 60, 68. Add `whitespace-nowrap` to each:

```html
@* Line 50 — Transaction badge *@
<td><span class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-blue-100 text-blue-800 whitespace-nowrap">Transaction</span></td>

@* Line 60 — Transfer badge *@
<td><span class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-purple-100 text-purple-800 whitespace-nowrap">Transfer</span></td>

@* Line 68 — Liability Payment badge *@
<td><span class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-orange-100 text-orange-800 whitespace-nowrap">Liability Payment</span></td>
```

- [ ] **Step 2: Replace LiabilityPayment status cell with React component**

The status `<td>` (lines 75–95) currently has a branch:
- Non-LiabilityPayment rows → `<div data-react="cleared-badge" ...>` ✅
- LiabilityPayment rows → plain Razor spans ❌

Replace the entire status `<td>` block with a single unified div. `data-movement-type="transaction"` is correct for liability payments — the API endpoint `/api/movements/{id}/cleared` accepts type `transaction` for both:

```html
<td>
    @{
        var mvType = item.MovementType == MovementType.Transfer ? "transfer" : "transaction";
    }
    <div data-react="cleared-badge"
         data-id="@item.Id"
         data-movement-type="@mvType"
         data-cleared="@item.IsCleared.ToString().ToLower()"></div>
</td>
```

This replaces the entire old block (lines 75–95):
```html
@* DELETE THIS ENTIRE BLOCK and replace with the above *@
<td>
    @if (item.MovementType != MovementType.LiabilityPayment)
    {
        var mvType = item.MovementType == MovementType.Transfer ? "transfer" : "transaction";
        <div data-react="cleared-badge"
             data-id="@item.Id"
             data-movement-type="@mvType"
             data-cleared="@item.IsCleared.ToString().ToLower()"></div>
    }
    else
    {
        @if (item.IsCleared)
        {
            <span class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-green-100 text-green-800">Cleared</span>
        }
        else
        {
            <span class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-amber-100 text-amber-800">Pending</span>
        }
    }
</td>
```

- [ ] **Step 3: Fix filter bar inputs to w-auto**

Lines 10–20. Add `w-auto` to the select and both date inputs:

```html
<form asp-action="Index" method="get" class="filter-bar">
    <select name="accountId" class="form-control w-auto">
        <option value="">All Accounts</option>
        @foreach (var item in (SelectList)ViewBag.Accounts)
        {
            <option value="@item.Value" selected="@(item.Value == ViewBag.AccountId?.ToString())">@item.Text</option>
        }
    </select>
    <input type="date" name="from" value="@ViewBag.From" class="form-control w-auto" />
    <input type="date" name="to" value="@ViewBag.To" class="form-control w-auto" />
    <button type="submit" class="btn btn-secondary">Filter</button>
    <a asp-action="Index" class="btn btn-link">Clear</a>
</form>
```

- [ ] **Step 4: Right-align Amount header and all three amount cells**

Add `text-right` to the Amount `<th>` (line 36) and to each of the three Amount `<td>` variants:

```html
@* Amount <th> — add text-right *@
<th class="text-right">Amount</th>

@* Transaction amount td (line 54) — add text-right *@
<td class="@(item.CategoryTypeName == "Income" ? "amount-income" : "amount-expense") whitespace-nowrap text-right">
    @item.CurrencySymbol @NumberFormatHelper.FormatAmount(item.Amount, ViewData["NumberFormat"] as string)
</td>

@* Transfer amount td (line 64) — add text-right *@
<td class="amount-neutral whitespace-nowrap text-right">@item.CurrencySymbol @NumberFormatHelper.FormatAmount(item.Amount, ViewData["NumberFormat"] as string)</td>

@* Liability Payment amount td (line 72) — add text-right *@
<td class="amount-neutral whitespace-nowrap text-right">@NumberFormatHelper.FormatAmount(item.Amount, ViewData["NumberFormat"] as string)</td>
```

- [ ] **Step 5: Build**

```bash
dotnet build ProjectCeres
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres/Views/Movements/Index.cshtml
git commit -m "fix: movements table — nowrap badges, react cleared-badge for liability payments, inline filter, right-align amounts"
```

---

### Task 3: Fix Transactions/Index.cshtml — LiabilityPayment badge + amount right-align

**Files:**
- Modify: `ProjectCeres/Views/Transactions/Index.cshtml`

Two things: the "Liability Payment" type string at line 71 needs to be an orange nowrap badge, and the Amount column needs `text-right`.

- [ ] **Step 1: Wrap "Liability Payment" type in orange badge**

Line 71 currently: `<td>Liability Payment</td>`

Replace with:

```html
<td><span class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-orange-100 text-orange-800 whitespace-nowrap">Liability Payment</span></td>
```

Leave `@item.CategoryTypeName` on line 81 as plain text — "Expense" and "Income" don't need badges in this table.

- [ ] **Step 2: Right-align Amount header and both amount cells**

Amount `<th>` is at line 55. The LiabilityPayment amount `<td>` is at lines 72–74, and the regular transaction amount `<td>` is at lines 82–84.

```html
@* Amount <th> *@
<th class="text-right">Amount</th>

@* LiabilityPayment amount td (lines 72-74) *@
<td class="amount-neutral text-right">
    @NumberFormatHelper.FormatAmount(item.Amount, ViewData["NumberFormat"] as string)
</td>

@* Regular transaction amount td (lines 82-84) *@
<td class="@(item.CategoryTypeName == "Income" ? "amount-income" : "amount-expense") whitespace-nowrap text-right">
    @item.CurrencySymbol @NumberFormatHelper.FormatAmount(item.Amount, ViewData["NumberFormat"] as string)
</td>
```

- [ ] **Step 3: Build**

```bash
dotnet build ProjectCeres
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Views/Transactions/Index.cshtml
git commit -m "fix: transactions table — orange nowrap badge for liability payment type, right-align amounts"
```

---

### Task 4: Fix Transfers/Index.cshtml — amount right-align

**Files:**
- Modify: `ProjectCeres/Views/Transfers/Index.cshtml`

The Amount `<th>` is at line 24. The Amount `<td>` is at line 37.

- [ ] **Step 1: Add text-right to Amount header and cell**

```html
@* Amount <th> (line 24) *@
<th class="text-right">Amount</th>

@* Amount <td> (line 37) — add text-right *@
<td class="amount-mono whitespace-nowrap text-right">@(string.IsNullOrEmpty(t.SourceAccount.Currency.Symbol) ? t.SourceAccount.Currency.Code : t.SourceAccount.Currency.Symbol) @NumberFormatHelper.FormatAmount(t.Amount, ViewData["NumberFormat"] as string)</td>
```

- [ ] **Step 2: Build**

```bash
dotnet build ProjectCeres
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Views/Transfers/Index.cshtml
git commit -m "fix: transfers table — right-align amount column"
```

---

### Task 5: Fix Accounts/Index.cshtml — balance colored by sign

**Files:**
- Modify: `ProjectCeres/Views/Accounts/Index.cshtml`

The balance cell at line 36 currently outputs the formatted amount with no color class. We need to look up the balance first, then apply `amount-income` (green) if ≥ 0 or `amount-expense` (red) if < 0. `ViewBag.Balances` is a `Dictionary<Guid, decimal>` populated by the controller.

- [ ] **Step 1: Color balance by sign**

Replace line 36:

```html
@* BEFORE *@
<td>@NumberFormatHelper.FormatAmount(((Dictionary<Guid,decimal>)ViewBag.Balances)[account.Id], ViewData["NumberFormat"] as string)</td>

@* AFTER *@
@{ var bal = ((Dictionary<Guid,decimal>)ViewBag.Balances)[account.Id]; }
<td class="@(bal >= 0 ? "amount-income" : "amount-expense")">
    @NumberFormatHelper.FormatAmount(bal, ViewData["NumberFormat"] as string)
</td>
```

- [ ] **Step 2: Build**

```bash
dotnet build ProjectCeres
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Views/Accounts/Index.cshtml
git commit -m "fix: accounts table — color balance green/red by sign"
```

---

### Task 6: Fix Categories/Index.cshtml — status pill badges

**Files:**
- Modify: `ProjectCeres/Views/Categories/Index.cshtml`

The Status column at line 45 renders plain text "Active" / "Inactive". Replace with green/gray pill badges consistent with the Budgets and Goals tables.

- [ ] **Step 1: Replace text with pill badges**

Replace line 45:

```html
@* BEFORE *@
<td>@(category.IsActive ? "Active" : "Inactive")</td>

@* AFTER *@
<td>
    @if (category.IsActive)
    {
        <span class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-green-100 text-green-800">Active</span>
    }
    else
    {
        <span class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-gray-100 text-gray-600">Inactive</span>
    }
</td>
```

- [ ] **Step 2: Build**

```bash
dotnet build ProjectCeres
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres/Views/Categories/Index.cshtml
git commit -m "fix: categories table — status text to green/gray pill badges"
```

---

### Task 7: Run full test suite and verify

**Files:** none (verification only)

- [ ] **Step 1: Run server tests**

```bash
dotnet test ProjectCeres.Tests
```

Expected: `Passed! - Failed: 0, Passed: 314, Skipped: 0, Total: 314`

- [ ] **Step 2: Visual verification checklist**

Load each page and confirm:

| Page | Check |
|---|---|
| `/Movements` | Filter bar is one row; type badges don't wrap; "Liability Payment" rows show clock icon + toggle; amounts right-aligned |
| `/Transactions` | "Liability Payment" type shows orange badge; amounts right-aligned |
| `/Transfers` | Amount right-aligned |
| `/Accounts` | Positive balances green, negative balances red |
| `/Categories` | Status column shows green "Active" / gray "Inactive" pills |
| Any page | Content area is wider — no wrapping on account names, category names, descriptions |
