# CategorySelect Component Design

**Date:** 2026-04-29
**Phase:** 3 — SPA migration (transactions feature area)
**Status:** Approved

---

## Problem

The current Razor category dropdown has two UX problems:

1. All categories (Income and Expense) appear in a single flat list, forcing the user to scan the full list regardless of intent.
2. System-only categories ("Uncategorized Expense", "Uncategorized Income") appear as selectable options. They are assigned by the import pipeline only and must never be user-selectable.

The fix is deferred to the React SPA migration. The Razor forms are left as-is until transactions are ported (migration step 4). No changes to the Razor layer.

---

## Scope

A shared `CategorySelect` React component used in four forms:

- New Transaction (`/transactions/new`)
- Edit Transaction (`/transactions/:id/edit`)
- New Recurring Transaction (`/recurring/new`)
- Edit Recurring Transaction (`/recurring/:id/edit`)

---

## API Endpoint

`GET /api/v1/categories` — already listed in `api-contract.md` as a Phase 3 endpoint. Response shape (to be added to `api-contract.md` when the endpoint is built):

```json
{
  "data": [
    {
      "id": "20000000-0000-0000-0000-000000000002",
      "name": "Salary",
      "categoryType": "Income",
      "isSystem": false,
      "isActive": true,
      "lifestyleTag": null
    }
  ]
}
```

- Non-paginated — returns `data` only, no `meta` (consistent with `api-contract.md` rule for non-paginated list endpoints).
- The server filters `isActive = false` categories out of the response by default. Inactive categories are not shown in the picker.
- The server does **not** filter `isSystem` — the client filters them out. This keeps the endpoint general-purpose (e.g. reports may need to display system category names).
- `categoryType` is `"Income"` or `"Expense"` — the two seeded `CategoryType` names. No other values exist.
- `lifestyleTag` is included for future use (lifestyle filtering in reports); not used by `CategorySelect`.

---

## Component: `CategorySelect`

### Location

`ProjectCeres.Client/src/components/CategorySelect.tsx`

### Props

```ts
interface CategorySelectProps {
  value: string | null;           // currently selected category ID (null = none selected)
  onChange: (id: string | null) => void;
  defaultTab?: "Expense" | "Income"; // defaults to "Expense" if omitted
}
```

### Behaviour

**Data fetching:**
- Fetches `GET /api/v1/categories` once on mount.
- Filters out `isSystem = true` entries before rendering.
- Groups remaining categories into `"Income"` and `"Expense"` buckets by `categoryType`.

**Tab toggle:**
- Renders a pill-style toggle (`Expense` | `Income`) inline with the "Category" label on the same row, vertically centered via flexbox (`align-items: center`, `justify-content: space-between`).
- Active Expense tab: white pill, dark red label (`text-red-800`).
- Active Income tab: white pill, dark green label (`text-green-800`).
- Inactive tab: transparent, muted grey.
- The toggle sits inside a light grey pill container (`bg-gray-100`, `rounded-md`, `p-0.5`).

**Default tab (new forms):**
- `defaultTab` prop controls initial tab. Omitting it defaults to `"Expense"` since most transactions are expenses.

**Pre-selection (edit forms):**
- When `value` is provided and categories have loaded, the component infers the correct tab from the category's `categoryType` and activates it automatically. No `defaultTab` needed in edit forms.

**Tab switch resets selection:**
- Switching tabs always calls `onChange(null)` and resets the dropdown to `"— Select category —"`. Income and Expense categories are mutually exclusive — no category can belong to both.

**Dropdown:**
- A standard `<select>` rendered below the label row, full width, matching `.form-control` styling.
- Options are the categories for the active tab only. No optgroups needed since the tab already separates types.
- Placeholder option: `value=""` — `"— Select category —"`.

**Loading / error states:**
- While fetching: dropdown is disabled, placeholder reads `"Loading categories…"`.
- On fetch error: dropdown is disabled, placeholder reads `"Failed to load categories"`. No retry UI — the parent form's submit validation will catch an empty value.

### Visual layout

```
Category                          [ Expense | Income ]
┌──────────────────────────────────────────────────┐
│ — Select category —                            ▾ │
└──────────────────────────────────────────────────┘
```

The label row uses `display: flex; align-items: center; justify-content: space-between`. The pill toggle height (22px) matches the label's computed line-height so they sit on the same baseline.

---

## Integration in forms

Each of the four forms passes the current `categoryId` from form state as `value`, and updates form state via `onChange`:

```tsx
<CategorySelect
  value={formState.categoryId}
  onChange={(id) => setFormState(s => ({ ...s, categoryId: id }))}
/>
```

Edit forms supply no `defaultTab` — the component infers the correct tab from `value` once categories load.

New forms omit `value` (or pass `null`) and omit `defaultTab` to use the Expense default.

---

## `IsSystem` seed fix

The two Uncategorized categories are currently seeded with `IsSystem = false`, which causes them to appear in the Razor dropdown today (despite the `!c.IsSystem` filter in the controller, they slip through because the flag is wrong). They must be corrected to `IsSystem = true` in the EF seed data. This is a data fix — no schema migration required, only an `OnModelCreating` seed update that takes effect on the next `dotnet ef database update`.

This fix is independent of the React component and should be applied as soon as this spec is implemented, even while Razor forms are still active — it will cause the Uncategorized categories to disappear from the current Razor dropdown immediately, which is the correct behaviour.

---

## API contract update

When `GET /api/v1/categories` is implemented, add its response shape to the Phase 3 table in `docs/api-contract.md`. The shape is defined in the **API Endpoint** section above.

---

## Testing

| Test | Type | Notes |
|------|------|-------|
| Renders expense categories by default | Unit (Vitest + RTL) | Mock API response; assert Income tab not visible in select options |
| Switching to Income tab shows only income categories | Unit | Assert expense options absent |
| Switching tab resets selection to null | Unit | Assert `onChange(null)` called |
| Pre-selects correct tab from existing value | Unit | Pass an Income category ID as `value`; assert Income tab active |
| Filters out `isSystem = true` categories | Unit | Include system categories in mock; assert absent from options |
| Disabled while loading | Unit | Assert `<select disabled>` during fetch |
| IsSystem seed fix — Uncategorized categories absent from API response | Integration | Controller filters `isSystem = true`; verify via `GET /api/v1/categories` |
