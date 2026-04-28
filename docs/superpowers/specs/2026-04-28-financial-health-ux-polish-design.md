# Financial Health Card — UX Polish Design Spec

**Date:** 2026-04-28
**Status:** Approved

---

## Problem

The Financial Health card renders metrics as a flat `<dl class="stat-list">` — a vertical list of label/value pairs. Because the card spans the full dashboard width, the label/value pairs look orphaned with whitespace to the right. Values sit at inconsistent indentation. The "— Not enough data" empty state gives no guidance on what's missing.

---

## Solution

Replace the `<dl>` layout in `_HealthSnapshot.cshtml` with a **horizontal 4-column bar** — a single row divided into four equal cells by inner borders. Matches the existing white card style used by Net Worth, Month to Date, and Reminders.

---

## Layout

```
┌─────────────────────────────────────────────────────────────────┐
│ Financial Health                                                 │
│ ┌──────────────┬──────────────┬──────────────┬────────────────┐ │
│ │ SPENDABLE    │ RUNWAY       │ INCOME VS.   │ BUDGET BURN    │ │
│ │ BALANCE      │              │ AVG          │ RATE           │ │
│ │              │              │              │                │ │
│ │ € 4.102,85   │ Needs 6 mo   │ Needs 6 mo   │ 41,0%          │ │
│ │              │ of expense   │ of income    │                │ │
│ │              │ history      │ history      │                │ │
│ └──────────────┴──────────────┴──────────────┴────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

- Outer card: existing `dashboard-card` class (white, `border`, `rounded-lg`, `p-5`)
- Inner row: `border border-[var(--border)] rounded-lg overflow-hidden` with `grid grid-cols-4`
- Each cell: `p-[14px_18px] border-r border-[var(--border)] last:border-r-0`
- Label: `text-[10.5px] uppercase tracking-widest text-muted-foreground font-medium mb-2`
- Value (when data present): `text-lg font-bold font-mono` with color class
- Empty cells: `bg-muted/30` background to distinguish visually from populated cells

---

## Empty State Copy

| Metric | Empty state text |
|--------|-----------------|
| Runway | "Needs 6 months of expense history" |
| Income vs. Avg | "Needs 6 months of income history" |
| Spendable Balance | (always has data if accounts exist; existing null path kept) |
| Budget Burn Rate | (null only if no active category budgets; existing null path kept) |

Empty state styling: `text-[11.5px] italic text-muted-foreground leading-snug`

---

## Color Logic (unchanged from Stage 9)

| Metric | Green | Amber | Red |
|--------|-------|-------|-----|
| Spendable Balance | ≥ 0 | — | < 0 |
| Runway | > 6 months | 3–6 months | < 3 months |
| Income vs. Avg | delta > 0% | — | delta < 0% |
| Budget Burn Rate | < 50% | 50–80% | > 80% |

Use existing Tailwind classes: `text-green-600`, `text-amber-600`, `text-red-600`, `text-muted-foreground`.

---

## CSP eval() Warning

The browser console shows: *"Content Security Policy of your site blocks the use of 'eval' in JavaScript"*

**Root cause:** Recharts (used by the React chart components) uses `new Function()` internally. No explicit CSP header is set in `Program.cs` — the warning comes from the browser's own heuristics.

**Decision:** Ignore for now. If a real CSP header is added in Phase 3, add `unsafe-eval` to `script-src` to accommodate Recharts, or evaluate switching charting libraries at that point.

---

## Files Changed

| File | Change |
|------|--------|
| `ProjectCeres/Views/Dashboard/_HealthSnapshot.cshtml` | Replace `<dl class="stat-list">` with 4-column grid layout; update empty state copy |

No service, controller, or model changes needed — data shape is unchanged.

---

## Out of Scope

- Changing metric calculations
- Adding tooltips or expandable detail
- Responsive collapse to 2×2 on mobile (the card already scrolls horizontally on small screens via the dashboard grid)
