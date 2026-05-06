# Project Ceres — Phase 3 Responsive Design Plan

> **Diataxis type:** Reference — defines the responsive design strategy for Phase 3. This is a web-browser responsiveness plan, not a native mobile app plan.

## Index

1. [Goal](#goal)
2. [Breakpoint Strategy](#breakpoint-strategy)
3. [Navigation](#navigation)
4. [Data Tables](#data-tables)
5. [Forms](#forms)
6. [Charts](#charts)
7. [Surface Inventory](#surface-inventory)
8. [Implementation Order](#implementation-order)
9. [Open Questions](#open-questions)

---

**Gate: SPA migration must be underway or complete before responsive work begins.**

All responsive work is React + CSS. No Razor-layer responsive work is planned — the Razor layer is being deleted. See [planning-phase3-spa-migration.md](planning-phase3-spa-migration.md).

---

## Goal

Make the full Ceres web app usable and intentionally designed across every screen size accessible via a web browser: mobile phones (portrait and landscape), tablets, old laptops, new laptops, large monitors. This is separate from any future native mobile app.

"Mobile ready" means:
- Every surface has an intentional layout at every tier — not just "it doesn't break"
- No horizontal overflow on any screen at or above 320px
- Touch targets meet minimum size (44×44px) on mobile
- No content is hidden or inaccessible at any breakpoint

---

## Breakpoint Strategy

Three named tiers with fluid CSS within each tier.

| Tier | Range | Tailwind prefix | Design intent |
|---|---|---|---|
| `mobile` | < 640px | (default / no prefix) | Single-column, touch-optimized, card layouts |
| `tablet` | 640px – 1023px | `sm:` | Two-column where useful, table layouts begin |
| `desktop` | ≥ 1024px | `lg:` | Full layout — sidebar, multi-column, dense tables |

**Implementation philosophy:** use fluid CSS (`grid auto-fill`, `minmax`, `flex-wrap`, `clamp()`) within each tier so layouts stretch gracefully between breakpoints rather than snapping hard. Fixed breakpoints define *intent* per tier; fluid CSS handles everything in between.

The `md:` prefix (768px) may be used for fine-tuning within the tablet tier but is not a named design tier.

---

## Navigation

**Mobile and tablet (< 1024px):** hamburger icon in a top bar opens a drawer that slides in from the left. Drawer overlays content with a backdrop. Tapping outside or pressing the close button dismisses it.

**Desktop (≥ 1024px):** persistent sidebar (left) plus a horizontal top bar that hosts the brand mark, global search, and the quick-add `+` button. The sidebar is the primary navigation; the top bar is utility chrome. Resolved by shipping both during the Batch 1 app shell — see `ProjectCeres.Client/src/app/layout/AppLayout.tsx`.

**Requirements:**
- All navigation destinations reachable from the drawer on mobile
- Active route highlighted in the drawer
- Drawer closes on route change
- Focus is trapped inside the drawer while open (accessibility)
- Drawer is implemented as a shadcn/ui `Sheet` component (left side)

---

## Data Tables

Data-heavy surfaces (Transactions, Movements, Transfers) use a **card/list view on mobile** and a **standard table on tablet and desktop**.

### Mobile card layout (< 640px)

Each row becomes a stacked card. Cards show the most important fields; secondary fields are de-emphasised or hidden. The exact field priority per surface is an open question — see [Open Questions](#open-questions).

General card anatomy:
```
┌─────────────────────────────────┐
│ Primary label        Amount     │
│ Secondary label      Date       │
│ Tag / badge                     │
└─────────────────────────────────┘
```

- Cards are full-width with consistent padding
- Tap target for the whole card navigates to the detail/edit view
- Row actions (edit, delete) appear as an icon button on the right or via a long-press context menu — open question, see below

### Tablet and desktop (≥ 640px)

Standard table layout as currently designed. Column sorting, pagination, and filter bar are unchanged.

### Surfaces

| Surface | Mobile treatment | Tablet+ treatment |
|---|---|---|
| Transactions list | Card view | Table |
| Movements ledger | Card view | Table |
| Transfers list | Card view | Table |
| Accounts list | Card view | Table |
| Categories list | Card view | Table |
| Recurring Transactions list | Card view | Table |
| Reports tables | Card view | Table |

---

## Forms

**Default: full-page route on mobile, modal dialog on desktop.**

| Screen size | Form presentation |
|---|---|
| Mobile (< 640px) | Full-page — navigates to `/entity/new` or `/entity/:id/edit`. Browser back button returns to list. |
| Tablet (640–1023px) | Full-page (same as mobile) or modal — revisit once SPA visual is taking shape |
| Desktop (≥ 1024px) | Modal dialog (`Dialog` from shadcn/ui) |

**Fallback options (to revisit at Phase 3 kickoff once SPA visual exists):**
- **B) Bottom sheet on mobile, modal on desktop** — shadcn/ui `Sheet` slides up from bottom on mobile. Already listed in the Phase 3 component table.
- **C) Full-page everywhere** — simplest implementation, consistent across all sizes.

Lock in the default (A) at Phase 3 kickoff. If full-page forms feel awkward after the first few surfaces are built, switch to B or C consistently — do not mix patterns across surfaces.

---

## Charts

Charts (Net Worth, Income/Expense, Spending Donut, Cash Flow, Account Balances) reflow to **full-width with reduced height** on all screen sizes.

- Chart container: `width: 100%`, height defined per chart type with a minimum floor (open question — see below)
- No simplified mobile variant — the chart is always shown
- No horizontal scroll containers for charts
- Chart legends reflow below the chart on narrow screens if they don't fit beside it
- `aria-label` on all chart containers and `role="img"` — carried forward from Phase 2 standard

---

## Surface Inventory

Every Phase 3 surface and its responsive treatment. Grouped from [planning-phase3.md — Page and surface inventory](planning-phase3.md#3-page-and-surface-inventory).

**New in Phase 3:**

| Surface | Mobile | Tablet | Desktop |
|---|---|---|---|
| Login | Single-column centered card | Single-column centered card | Single-column centered card (narrow) |
| TOTP verification | Single-column centered card | Single-column centered card | Single-column centered card |
| Registration | Single-column centered card | Single-column centered card | Single-column centered card |
| Password reset | Single-column centered card | Single-column centered card | Single-column centered card |
| Guided onboarding wizard | Full-screen stepper | Full-screen stepper | Full-screen stepper (max-width constrained) |
| Active sessions list | Card view | Card view | Table |
| Support ticket form | Full-page form | Full-page or modal | Modal |
| Support ticket list | Card view | Card view | Table |

**Ported from Razor:**

| Surface | Mobile | Tablet | Desktop |
|---|---|---|---|
| Dashboard | Single-column card stack | 2-col grid | Multi-col grid |
| Transactions list | Card view + full-page form | Table + modal form | Table + modal form |
| Transfers list | Card view + full-page form | Table + modal form | Table + modal form |
| Movements ledger | Card view | Table | Table |
| Accounts list | Card view + full-page form | Table + modal form | Table + modal form |
| Categories list | Card view + full-page form | Table + modal form | Table + modal form |
| Category Budgets | Card view | Table | Table |
| Goal Budgets | Card view | Table | Table |
| Recurring Transactions | Card view + full-page form | Table + modal form | Table + modal form |
| CSV Import | Full-page stepper | Full-page stepper | Full-page stepper |
| CSV Export | Settings section | Settings section | Settings section |
| Reports | Single-column charts | Single-column charts | Multi-column layout |
| Settings / preferences | Single-column sections | Single-column sections | Two-column (nav + content) |

**React components (visual audit only):**

| Component | Responsive consideration |
|---|---|
| `CategoryBudgetBars` | Full-width reflow, bar labels truncate on narrow screens |
| `GoalBudgetBars` | Full-width reflow |
| `ClearedBadge` | No change needed |
| `NetWorthChart` | Full-width, reduced height |
| `IncomeExpenseChart` | Full-width, reduced height |
| `SpendingDonutChart` | Full-width, legend below on mobile |
| `AccountBalancesChart` | Full-width, reduced height |
| `CashFlowChart` | Full-width, reduced height |

---

## Implementation Order

Responsive work is not a separate phase — it is built into every surface as it is migrated during the SPA transition. Do not ship a surface without its mobile layout.

1. **Breakpoint tokens and fluid layout primitives** — define the three tiers in Tailwind config, build the app shell (drawer nav + layout zones) responsively from the start
2. **Auth screens** — first impression for beta users; simple single-column layouts, low responsive complexity
3. **Onboarding flow** — full-screen stepper, same layout across all sizes
4. **Dashboard** — highest visibility; grid reflow is the main challenge
5. **Transactions + Transfers + Movements** — card/list views on mobile, highest daily usage
6. **Budgets + Reports** — chart reflow, filter panels
7. **Accounts + Categories + Recurring Transactions** — management screens
8. **Settings + Sessions + Support tickets** — lowest frequency, simplest layouts

---

## Open Questions

These must be resolved at Phase 3 kickoff or during implementation of each surface.

- **Card field priority per table surface** — for each card/list view (Transactions, Movements, Transfers, Accounts, etc.), which fields are shown prominently, which are de-emphasised, and which are hidden? Decide surface-by-surface at implementation time.
- **Row actions on mobile cards** — inline icon button on the card, long-press context menu, or swipe-to-reveal actions? Pick one pattern and apply consistently.
- **Tablet form presentation** — default is full-page (same as mobile). Revisit at Phase 3 kickoff once the SPA visual is taking shape. Options: keep full-page, switch to bottom sheet, or switch to modal.
- **Chart minimum height on mobile** — what is the minimum readable height for each chart type on a 375px screen? Define per chart at implementation time.
- **Dashboard card grid on tablet** — 2-column assumed; confirm at kickoff whether some cards (e.g. Net Worth chart) should span full width at the tablet breakpoint.
- **Touch target audit** — after each surface is built, verify all interactive elements meet 44×44px minimum. Define a checklist item in the Definition of Done for each surface.
- **Responsive testing device matrix** — which physical or emulated devices are used to verify each surface? At minimum: iPhone SE (375px), iPhone 14 (390px), iPad (768px), 13" laptop (1280px), 27" monitor (1920px+ or 2560px+).
