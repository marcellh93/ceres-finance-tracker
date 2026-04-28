# Table UX Polish — Design Spec

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Fix badge wrapping, restore interactive cleared toggle on Liability Payment rows, widen the page container so tables stop wrapping on desktop, and apply consistent visual polish across all data tables.

**Architecture:** Pure CSS + Razor view changes. No new services, migrations, or API changes. The React `ClearedBadge` component already supports `data-movement-type="transaction"` for liability payments — only the Razor view needs updating.

**Tech Stack:** Tailwind CSS v3 (`app.css` `@layer components`), ASP.NET Core MVC Razor (`.cshtml`), existing React `ClearedBadge` component.

---

## Files Affected

| File | Change |
|---|---|
| `ProjectCeres/Styles/app.css` | `max-w-6xl` → `max-w-screen-xl` on `.page-content` |
| `ProjectCeres/Views/Movements/Index.cshtml` | Badge nowrap; LiabilityPayment status → React component; filter bar inline; amount right-align |
| `ProjectCeres/Views/Transactions/Index.cshtml` | LiabilityPayment type badge nowrap; amount right-align |
| `ProjectCeres/Views/Transfers/Index.cshtml` | Amount right-align |
| `ProjectCeres/Views/Accounts/Index.cshtml` | Balance colored by sign |
| `ProjectCeres/Views/Categories/Index.cshtml` | Status column: text → pill badges |

---

## P1 — Broken / Clearly Wrong

### 1. Type badges must not wrap

All type badge spans in Movements gain `whitespace-nowrap`:

```html
<!-- Transaction -->
<span class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-blue-100 text-blue-800 whitespace-nowrap">Transaction</span>

<!-- Transfer -->
<span class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-purple-100 text-purple-800 whitespace-nowrap">Transfer</span>

<!-- Liability Payment -->
<span class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-orange-100 text-orange-800 whitespace-nowrap">Liability Payment</span>
```

### 2. Liability Payment status → React ClearedBadge

In `Movements/Index.cshtml`, the LiabilityPayment branch currently renders a plain Razor span for status. Replace it with the React component:

```html
@* BEFORE — plain span, no icon, not interactive *@
@if (item.IsCleared)
{
    <span class="...">Cleared</span>
}
else
{
    <span class="...">Pending</span>
}

@* AFTER — same React component as Transaction/Transfer rows *@
<div data-react="cleared-badge"
     data-id="@item.Id"
     data-movement-type="transaction"
     data-cleared="@item.IsCleared.ToString().ToLower()"></div>
```

`data-movement-type="transaction"` is correct — the API endpoint for liability payments is the same `/api/movements/{id}/cleared` with type `transaction`.

### 3. Transactions — LiabilityPayment type column

In `Transactions/Index.cshtml`, the "Liability Payment" type is rendered as plain text in the Type column. Wrap it in a nowrap span consistent with the Movements badge:

```html
@* BEFORE *@
<td>Liability Payment</td>

@* AFTER *@
<td><span class="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-orange-100 text-orange-800 whitespace-nowrap">Liability Payment</span></td>
```

Regular transactions show category type name ("Expense", "Income") as plain text — leave those as-is. Only the special-cased "Liability Payment" string needs the badge.

---

## P2 — Container Width

In `app.css`, one character change:

```css
/* BEFORE */
.page-content { @apply max-w-6xl mx-auto px-6 py-8; }

/* AFTER */
.page-content { @apply max-w-screen-xl mx-auto px-6 py-8; }
```

`max-w-screen-xl` = 80rem (1280px). Up from 72rem (1152px). All pages benefit — no per-table changes needed.

---

## P3 — Polish

### Amount columns — right-align

In `Movements/Index.cshtml`, `Transactions/Index.cshtml`, and `Transfers/Index.cshtml`:
- Add `text-right` to the Amount `<th>` header
- Add `text-right` to every Amount `<td>` cell (both the colored income/expense cells and the neutral ones)

### Categories — Status pill badges

In `Categories/Index.cshtml`, replace plain text status with badges:

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

### Accounts — Balance colored by sign

In `Accounts/Index.cshtml`, apply color class based on balance value:

```html
@* BEFORE *@
<td>@NumberFormatHelper.FormatAmount(...)</td>

@* AFTER *@
@{
    var bal = ((Dictionary<Guid,decimal>)ViewBag.Balances)[account.Id];
}
<td class="@(bal >= 0 ? "amount-income" : "amount-expense")">
    @NumberFormatHelper.FormatAmount(bal, ViewData["NumberFormat"] as string)
</td>
```

### Movements filter bar — inline layout

The Movements filter already uses `class="filter-bar"` but `.form-control` applies `block w-full` which overrides flex sizing and causes each input to fill the full row. Fix by adding `w-auto` to the date inputs so they shrink to fit:

```html
<form asp-action="Index" method="get" class="filter-bar">
    <select name="accountId" class="form-control w-auto">...</select>
    <input type="date" name="from" value="@ViewBag.From" class="form-control w-auto" />
    <input type="date" name="to" value="@ViewBag.To" class="form-control w-auto" />
    <button type="submit" class="btn btn-secondary">Filter</button>
    <a asp-action="Index" class="btn btn-link">Clear</a>
</form>
```

---

## Out of Scope

- RecurringTransactions estimated amount currency symbol — the `RecurringTransaction` model doesn't eagerly load `Account.Currency` in the current service query; requires a service-layer change, deferred to a separate pass.
- Report table amount alignment — BudgetVsActual already has `text-right` on amount columns; LargestExpenses, MonthlyCashFlow, NetWorthOverTime are report-only views and already readable.

---

## Testing

- `dotnet build` — verifies Razor compiles
- `dotnet test` — 314 server tests must stay green
- Visual: load Movements, Transactions, Transfers, Accounts, Categories pages and verify no wrapping, correct badge colors, correct cleared toggle behavior on liability payment rows
