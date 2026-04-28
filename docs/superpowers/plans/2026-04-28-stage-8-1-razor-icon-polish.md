# Stage 8.1 — Razor Icon & Token Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Lucide icons to all action buttons across every Razor view and replace bare text links in action cells with styled `btn btn-sm` buttons, so the Razor pages look visually coherent next to the existing React/shadcn components.

**Architecture:** This is a pure HTML/Razor markup change — no C# changes, no new services, no migrations. Every SVG is inlined directly (no npm install needed in the Razor project). The Razor project uses Tailwind v3 with an existing component layer (`btn`, `btn-sm`, `btn-secondary`, `btn-danger`, `btn-primary`, `link-danger`, `actions`) defined in `ProjectCeres/Styles/app.css`. We reuse those classes everywhere; no new CSS is added. Transactions, Transfers, Movements, and Budgets already have icons on some buttons — those are reference implementations to stay consistent with.

**Tech Stack:** ASP.NET Core 10 Razor (`.cshtml`), Tailwind CSS v3, inline SVG (Lucide icon paths hand-copied).

---

## SVG Icon Reference

Use these exact SVG snippets everywhere. Do NOT vary them — consistency matters.

**pencil (Edit):**
```html
<svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 3a2.828 2.828 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5L17 3z"/></svg>
```

**trash-2 (Delete / Remove attachment):**
```html
<svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14H6L5 6"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4h6v2"/></svg>
```

**power-off (Deactivate):**
```html
<svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18.36 6.64a9 9 0 1 1-12.73 0"/><line x1="12" y1="2" x2="12" y2="12"/></svg>
```

**check (Confirm reminder):**
```html
<svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
```

**x (Dismiss reminder):**
```html
<svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
```

**rotate-ccw (Recover / Undo):**
```html
<svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1 4 1 10 7 10"/><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"/></svg>
```

**plus (New / Add):**
```html
<svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
```

**book-open (Ledger):**
```html
<svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z"/><path d="M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z"/></svg>
```

---

## Button pattern reference

**Action cell button (small, secondary — Edit):**
```html
<a asp-action="Edit" asp-route-id="@item.Id" class="btn btn-sm btn-secondary inline-flex items-center gap-1">
    <svg ...pencil svg...></svg>
    Edit
</a>
```

**Action cell button (small, danger — Delete/Deactivate):**
```html
<a asp-action="Deactivate" asp-route-id="@item.Id" class="btn btn-sm btn-danger inline-flex items-center gap-1">
    <svg ...power-off svg...></svg>
    Deactivate
</a>
```

**Page header "New X" button (primary, full-size):**
```html
<a asp-action="Create" class="btn btn-primary inline-flex items-center gap-1.5">
    <svg ...plus svg (h-4 w-4)...></svg>
    New X
</a>
```

Note: `btn-sm` is not defined in `app.css` yet — add it in Task 1.

---

## Files modified

| File | Change |
|---|---|
| `ProjectCeres/Styles/app.css` | Add `.btn-sm` utility class |
| `ProjectCeres/Views/Accounts/Index.cshtml` | Icons on Ledger, Edit, Deactivate; style New Account |
| `ProjectCeres/Views/Categories/Index.cshtml` | Icons on Edit, Deactivate; style New Category |
| `ProjectCeres/Views/RecurringTransactions/_ReminderTable.cshtml` | Icons on Confirm, Dismiss, Edit, Deactivate |
| `ProjectCeres/Views/RecurringTransactions/Upcoming.cshtml` | Icons on Confirm, Dismiss |
| `ProjectCeres/Views/RecurringTransactions/Index.cshtml` | Style New Reminder, Upcoming Payments buttons |
| `ProjectCeres/Views/Settings/Edit.cshtml` | Icon on Save button |
| `ProjectCeres/Views/Accounts/Deactivate.cshtml` | Icons on confirmation form buttons |
| `ProjectCeres/Views/Categories/Deactivate.cshtml` | Icons on confirmation form buttons |
| `ProjectCeres/Views/Budgets/Goals.cshtml` | Icons on Edit, Deactivate |
| `ProjectCeres/Views/CsvImportProfiles/Index.cshtml` | Wrap bare SVG+text links in `btn btn-sm` classes |

Views already done (Transactions, Transfers, Movements, Budgets/Index) — **do not touch these**.

---

## Task 1: Add `.btn-sm` to CSS

**Files:**
- Modify: `ProjectCeres/Styles/app.css`

There is no `.btn-sm` class yet, but it's referenced by the existing Transactions/Transfers/Movements/Budgets views. Add it next to the existing button definitions.

- [ ] **Step 1: Open `ProjectCeres/Styles/app.css` and find the buttons block**

Look for the line:
```css
.btn            { @apply inline-flex items-center px-4 py-2 rounded-md text-sm font-medium transition-colors focus:outline-none focus:ring-2 focus:ring-offset-2 no-underline; }
```

- [ ] **Step 2: Add `.btn-sm` immediately after `.btn-link`**

Insert this line after `.btn-link`:
```css
.btn-sm         { @apply px-2.5 py-1 text-xs rounded; }
```

- [ ] **Step 3: Build CSS to confirm no errors**

Run from `ProjectCeres/` directory:
```bash
dotnet build ProjectCeres
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Styles/app.css
git commit -m "style: add btn-sm utility class to CSS"
```

---

## Task 2: Accounts Index — icons on all action buttons

**Files:**
- Modify: `ProjectCeres/Views/Accounts/Index.cshtml`

Currently the actions column has bare text links: `Ledger`, `Edit`, `Deactivate`. Replace with styled buttons.

- [ ] **Step 1: Replace the actions cell**

Replace:
```html
                    <td class="actions">
                        <a asp-action="Ledger" asp-route-id="@account.Id">Ledger</a>
                        @if (account.IsActive)
                        {
                            <a asp-action="Edit" asp-route-id="@account.Id">Edit</a>
                            <a asp-action="Deactivate" asp-route-id="@account.Id">Deactivate</a>
                        }
                    </td>
```

With:
```html
                    <td class="actions">
                        <a asp-action="Ledger" asp-route-id="@account.Id" class="btn btn-sm btn-secondary inline-flex items-center gap-1">
                            <svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z"/><path d="M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z"/></svg>
                            Ledger
                        </a>
                        @if (account.IsActive)
                        {
                            <a asp-action="Edit" asp-route-id="@account.Id" class="btn btn-sm btn-secondary inline-flex items-center gap-1">
                                <svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 3a2.828 2.828 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5L17 3z"/></svg>
                                Edit
                            </a>
                            <a asp-action="Deactivate" asp-route-id="@account.Id" class="btn btn-sm btn-danger inline-flex items-center gap-1">
                                <svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18.36 6.64a9 9 0 1 1-12.73 0"/><line x1="12" y1="2" x2="12" y2="12"/></svg>
                                Deactivate
                            </a>
                        }
                    </td>
```

- [ ] **Step 2: Update the "New Account" button in the page header**

Replace:
```html
    <a asp-action="Create" class="btn btn-primary">+ New Account</a>
```
With:
```html
    <a asp-action="Create" class="btn btn-primary inline-flex items-center gap-1.5">
        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
        New Account
    </a>
```

- [ ] **Step 3: Build to confirm no errors**

```bash
dotnet build ProjectCeres
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Views/Accounts/Index.cshtml
git commit -m "style: add icons to Accounts index action buttons"
```

---

## Task 3: Categories Index — icons on all action buttons

**Files:**
- Modify: `ProjectCeres/Views/Categories/Index.cshtml`

Currently the actions column has bare text links with no classes: `Edit`, `Deactivate`.

- [ ] **Step 1: Replace the actions cell inside the category loop**

Replace:
```html
                        <td class="actions">
                            @if (!category.IsSystem && category.IsActive)
                            {
                                <a asp-action="Edit" asp-route-id="@category.Id">Edit</a>
                                <a asp-action="Deactivate" asp-route-id="@category.Id">Deactivate</a>
                            }
                        </td>
```
With:
```html
                        <td class="actions">
                            @if (!category.IsSystem && category.IsActive)
                            {
                                <a asp-action="Edit" asp-route-id="@category.Id" class="btn btn-sm btn-secondary inline-flex items-center gap-1">
                                    <svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 3a2.828 2.828 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5L17 3z"/></svg>
                                    Edit
                                </a>
                                <a asp-action="Deactivate" asp-route-id="@category.Id" class="btn btn-sm btn-danger inline-flex items-center gap-1">
                                    <svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18.36 6.64a9 9 0 1 1-12.73 0"/><line x1="12" y1="2" x2="12" y2="12"/></svg>
                                    Deactivate
                                </a>
                            }
                        </td>
```

- [ ] **Step 2: Update the "New Category" button**

Replace:
```html
    <a asp-action="Create" class="btn btn-primary">+ New Category</a>
```
With:
```html
    <a asp-action="Create" class="btn btn-primary inline-flex items-center gap-1.5">
        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
        New Category
    </a>
```

- [ ] **Step 3: Build to confirm no errors**

```bash
dotnet build ProjectCeres
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Views/Categories/Index.cshtml
git commit -m "style: add icons to Categories index action buttons"
```

---

## Task 4: Recurring Transactions — icons across all reminder views

**Files:**
- Modify: `ProjectCeres/Views/RecurringTransactions/_ReminderTable.cshtml`
- Modify: `ProjectCeres/Views/RecurringTransactions/Upcoming.cshtml`
- Modify: `ProjectCeres/Views/RecurringTransactions/Index.cshtml`

`_ReminderTable.cshtml` is the shared partial used by the Due Now / Upcoming sections. It has bare links: `Confirm`, `Dismiss`, `Edit`, `Deactivate`. `Upcoming.cshtml` also has bare `Confirm`, `Dismiss` links.

- [ ] **Step 1: Update `_ReminderTable.cshtml` actions cell**

Replace:
```html
                <td class="actions">
                    <a asp-action="Confirm" asp-route-id="@r.Id">Confirm</a>
                    <a asp-action="Dismiss" asp-route-id="@r.Id">Dismiss</a>
                    <a asp-action="Edit" asp-route-id="@r.Id">Edit</a>
                    <a asp-action="Deactivate" asp-route-id="@r.Id" class="link-danger">Deactivate</a>
                </td>
```
With:
```html
                <td class="actions">
                    <a asp-action="Confirm" asp-route-id="@r.Id" class="btn btn-sm btn-secondary inline-flex items-center gap-1">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                        Confirm
                    </a>
                    <a asp-action="Dismiss" asp-route-id="@r.Id" class="btn btn-sm btn-secondary inline-flex items-center gap-1">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                        Dismiss
                    </a>
                    <a asp-action="Edit" asp-route-id="@r.Id" class="btn btn-sm btn-secondary inline-flex items-center gap-1">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 3a2.828 2.828 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5L17 3z"/></svg>
                        Edit
                    </a>
                    <a asp-action="Deactivate" asp-route-id="@r.Id" class="btn btn-sm btn-danger inline-flex items-center gap-1">
                        <svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18.36 6.64a9 9 0 1 1-12.73 0"/><line x1="12" y1="2" x2="12" y2="12"/></svg>
                        Deactivate
                    </a>
                </td>
```

- [ ] **Step 2: Update `Upcoming.cshtml` actions cell**

Replace:
```html
                    <td class="actions">
                        <a asp-action="Confirm" asp-route-id="@r.Id">Confirm</a>
                        <a asp-action="Dismiss" asp-route-id="@r.Id">Dismiss</a>
                    </td>
```
With:
```html
                    <td class="actions">
                        <a asp-action="Confirm" asp-route-id="@r.Id" class="btn btn-sm btn-secondary inline-flex items-center gap-1">
                            <svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
                            Confirm
                        </a>
                        <a asp-action="Dismiss" asp-route-id="@r.Id" class="btn btn-sm btn-secondary inline-flex items-center gap-1">
                            <svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                            Dismiss
                        </a>
                    </td>
```

- [ ] **Step 3: Update `Index.cshtml` page header buttons**

Replace:
```html
        <a asp-action="Upcoming" class="btn btn-secondary">Upcoming Payments</a>
        <a asp-action="Create" class="btn btn-primary">+ New Reminder</a>
```
With:
```html
        <a asp-action="Upcoming" class="btn btn-secondary inline-flex items-center gap-1.5">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>
            Upcoming Payments
        </a>
        <a asp-action="Create" class="btn btn-primary inline-flex items-center gap-1.5">
            <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            New Reminder
        </a>
```

- [ ] **Step 4: Build to confirm no errors**

```bash
dotnet build ProjectCeres
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres/Views/RecurringTransactions/_ReminderTable.cshtml \
        ProjectCeres/Views/RecurringTransactions/Upcoming.cshtml \
        ProjectCeres/Views/RecurringTransactions/Index.cshtml
git commit -m "style: add icons to Recurring Transactions action buttons"
```

---

## Task 5: Settings, Deactivate confirmation pages, Budgets Goals

**Files:**
- Modify: `ProjectCeres/Views/Settings/Edit.cshtml`
- Modify: `ProjectCeres/Views/Accounts/Deactivate.cshtml`
- Modify: `ProjectCeres/Views/Categories/Deactivate.cshtml`
- Modify: `ProjectCeres/Views/Budgets/Goals.cshtml`

These are small forms with bare submit/cancel buttons, plus the Goals page which needs to be read before editing.

- [ ] **Step 1: Read `Budgets/Goals.cshtml` to see current state**

```bash
cat ProjectCeres/Views/Budgets/Goals.cshtml
```

- [ ] **Step 2: Update `Settings/Edit.cshtml` Save button**

Replace:
```html
    <button type="submit" class="btn btn-primary">Save Settings</button>
```
With:
```html
    <button type="submit" class="btn btn-primary inline-flex items-center gap-1.5">
        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"/></svg>
        Save Settings
    </button>
```

- [ ] **Step 3: Update `Accounts/Deactivate.cshtml` confirm/cancel buttons**

Replace:
```html
    <button type="submit" class="btn btn-danger">Yes, Deactivate</button>
    <a asp-action="Index" class="btn btn-secondary">Cancel</a>
```
With:
```html
    <button type="submit" class="btn btn-danger inline-flex items-center gap-1.5">
        <svg xmlns="http://www.w3.org/2000/svg" class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M18.36 6.64a9 9 0 1 1-12.73 0"/><line x1="12" y1="2" x2="12" y2="12"/></svg>
        Yes, Deactivate
    </button>
    <a asp-action="Index" class="btn btn-secondary">Cancel</a>
```

- [ ] **Step 4: Read `Categories/Deactivate.cshtml` then apply the same pattern**

Read the file to confirm its exact button markup, then replace the submit button with the same power-off icon pattern as Step 3 above. The cancel link stays as-is.

- [ ] **Step 5: Update `Budgets/Goals.cshtml` — add icons based on what you read in Step 1**

After reading the file in Step 1, apply the same icon pattern:
- New Goal button in page header → plus icon, `inline-flex items-center gap-1.5`
- Edit buttons in actions cell → pencil icon, `btn btn-sm btn-secondary inline-flex items-center gap-1`
- Deactivate buttons in actions cell → power-off icon, `btn btn-sm btn-danger inline-flex items-center gap-1`

- [ ] **Step 6: Build to confirm no errors**

```bash
dotnet build ProjectCeres
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add ProjectCeres/Views/Settings/Edit.cshtml \
        ProjectCeres/Views/Accounts/Deactivate.cshtml \
        ProjectCeres/Views/Categories/Deactivate.cshtml \
        ProjectCeres/Views/Budgets/Goals.cshtml
git commit -m "style: add icons to Settings, Deactivate confirms, and Goal Budgets"
```

---

## Task 6: CsvImportProfiles — upgrade bare SVG links to btn classes

**Files:**
- Modify: `ProjectCeres/Views/CsvImportProfiles/Index.cshtml`

The CsvImportProfiles index already has inline SVGs on Edit and Delete, but the links have no button classes — they render as plain text links with an icon. Wrap them in `btn btn-sm` classes to match the rest of the app. The Recover button uses `btn-link` which is fine — keep it.

- [ ] **Step 1: Replace the Edit link in the actions cell**

Replace:
```html
                        <a asp-action="Edit" asp-route-id="@profile.Id">
                            <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
                            Edit
                        </a>
```
With:
```html
                        <a asp-action="Edit" asp-route-id="@profile.Id" class="btn btn-sm btn-secondary inline-flex items-center gap-1">
                            <svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 3a2.828 2.828 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5L17 3z"/></svg>
                            Edit
                        </a>
```

- [ ] **Step 2: Replace the Delete link in the actions cell**

Replace:
```html
                        <a asp-action="Delete" asp-route-id="@profile.Id">
                            <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/></svg>
                            Delete
                        </a>
```
With:
```html
                        <a asp-action="Delete" asp-route-id="@profile.Id" class="btn btn-sm btn-danger inline-flex items-center gap-1">
                            <svg xmlns="http://www.w3.org/2000/svg" class="h-3.5 w-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14H6L5 6"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4h6v2"/></svg>
                            Delete
                        </a>
```

- [ ] **Step 3: Build to confirm no errors**

```bash
dotnet build ProjectCeres
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres/Views/CsvImportProfiles/Index.cshtml
git commit -m "style: wrap CsvImportProfiles action links in btn classes"
```

---

## Task 7: Run full test suite and verify

**Files:** None modified.

This stage has no server-side logic changes, so integration tests must all still pass.

- [ ] **Step 1: Run all server tests**

```bash
dotnet test
```
Expected: All tests pass, 0 failed. (Currently 314 tests.)

- [ ] **Step 2: Run client tests**

```bash
pnpm --dir ProjectCeres.Client test
```
Expected: All 32 tests pass.

- [ ] **Step 3: If any test fails, investigate**

There should be no failures — this stage only modifies `.cshtml` markup. If a test fails, read the error carefully. It almost certainly means a file was accidentally modified beyond `.cshtml`. Do not proceed until 0 failures.
