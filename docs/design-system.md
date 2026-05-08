# Project Ceres — Design System

> **Diataxis type:** Reference — source of truth for every visual token used by the React client. If a value is not listed here, it must not be hard-coded in components.

## Index

1. [How to use this document](#how-to-use-this-document)
2. [Color palette](#color-palette)
3. [Typography](#typography)
4. [Spacing, radius, shadow](#spacing-radius-shadow)
5. [Motion](#motion)
6. [Chart palette](#chart-palette)
7. [shadcn/ui overrides](#shadcnui-overrides)
8. [The `<Numeric>` component](#the-numeric-component)
9. [Status badges](#status-badges)
10. [Layout primitives](#layout-primitives)
11. [App Shell](#app-shell)
12. [Forms & validation](#forms--validation)
13. [Combobox recipe](#combobox-recipe)
14. [DatePickerField](#datepickerfield)
15. [Money input](#money-input)
16. [Delta / PriorComparison row](#delta--priorcomparison-row)
17. [Tooltipped panel labels & empty states](#tooltipped-panel-labels--empty-states)
18. [PagePlaceholder & route-change focus](#pageplaceholder--route-change-focus)
19. [Status block (success / idle side panel)](#status-block-success--idle-side-panel)
20. [AttachmentDropzone](#attachmentdropzone)
21. [The `<CardError>` component](#the-carderror-component)
22. [Toasts](#toasts)
23. [Showcase route](#showcase-route)
24. [Known browser console messages](#known-browser-console-messages)
25. [Known limitations](#known-limitations)

---

## How to use this document

Every visual decision lives as a CSS custom property in `ProjectCeres.Client/src/index.css`. Tailwind v4 reads these via the `@theme inline` block and exposes them as utilities (`bg-primary`, `text-muted-foreground`, etc.). Components must use the utilities, never hex literals.

To retune a color or any other token, edit `index.css` only — no component changes required.

The internal `/design-system.html` route renders every token live and doubles as a visual regression check. Open it during local development to verify any token change.

### Working rules (for any frontend change)

1. **Use existing tokens and recipes.** If a token, variant, or recipe already covers what you're building, reuse it — don't invent a parallel one. Hex literals, ad-hoc spacing, hand-rolled versions of documented primitives all count as violations.
2. **Missing tokens go in `index.css` first.** If a needed token, badge variant, or recipe is genuinely absent, add it to `index.css` and document it here *before* building the consumer. Components that ship with private one-off values become future inconsistency debt.
3. **Invoke the `frontend-design` skill** for visual decisions, and `vercel-react-best-practices` for React implementation. They're complementary, not redundant: `frontend-design` enforces interaction-state, affordance, mobile, and copy quality; `vercel-react-best-practices` covers re-render hygiene, bundle/import patterns, and rendering perf. Skip the `server-*` rule family (no RSC) and read `bundle-dynamic-imports` as `React.lazy` (no `next/dynamic`). The React Native skill does not apply — the client is web-only.
4. **Show the rendered result and wait for explicit approval** before committing layout, copy, or hierarchy changes. Pre-votes inside an option menu do not count as consent.
5. **Cross-codebase consistency.** A change to a shared primitive must be applied everywhere it's used in the same pass — never leave one page on the old version and another on the new.
6. **View transitions.** The project uses *CSS-based* view transitions today (`<feature>-row-<id>` / `<feature>-form` naming — see [Motion → View transition naming](#view-transition-naming)). React's `<ViewTransition>` component (covered by `vercel-react-view-transitions`) requires `react@canary`; we're on stable. Treat that skill as a reference for the eventual canary-React migration, not as a default trigger today.
7. **Motion tokens.** All transition and animation durations must reference the motion tokens (`--motion-duration-fast/base/slow`). Don't introduce raw `duration-200` literals or hardcoded ms values — every animated property in the SPA goes through one of those three tokens. See [Motion](#motion) for the token table and easing functions.
8. **Pre-commit audit.** Run `web-design-guidelines` against the changed files before commit — it covers accessibility, focus states, form patterns, content overflow, hydration, and motion preferences. Treat its output as a tripwire, not a fresh rule source: many of its rules are already encoded in this document (tabular numerals via `<Numeric>`, ellipsis character `…` in copy, inline form errors, the `outline-none` + `tabIndex={-1}` route-focus pattern). Re-flagged items signal drift from the design system, not a new requirement.
9. **UX/UI verification checklist — required after every implementation.** Start `dotnet run` + `pnpm dev` and open each changed page in the browser. Explicitly verify:
   - **Golden path** — primary action works, result looks correct.
   - **Layout context** — content inside `<Outlet>` is not obscured by sticky ancestors (navbars, filter bars, sidebars). A component that looks correct in isolation can be buried in context.
   - **Empty state** — page renders sensibly with no data.
   - **Error state** — `<CardError>` fires and retry works.
   - **Mobile 375px** — layout collapses correctly; no horizontal overflow.
   - **Navigation** — all back buttons, breadcrumbs, and pagination links go to the right place.
   If browser access is unavailable, say so explicitly and hand this checklist to the user with the specific URLs and actions to verify. Never silently skip it.

---

## Color palette

The palette is defined in OKLCH for perceptual uniformity. Values land in two blocks: `:root` (light) and `.dark` (dark).

**Brand intent:**
- **Neutrals:** Zinc — cool, slightly warmer than slate; reads as calm/professional.
- **Primary:** Deep teal — distinctive (not the typical SaaS blue), reads as money/calm without being literal green.
- **Success:** Emerald — used for income, positive deltas, "cleared" states.
- **Destructive:** Rose — used for expense, delete actions, validation errors.
- **Warning:** Amber — used for "needs review" / pending states.
- **Info:** Sky.
- **Violet (chart-6):** Now load-bearing for **Transfer** rows in Movements (de facto semantic — see Chart palette below).
- **Orange (chart-7):** Now load-bearing for **Liability Payment** rows in Movements (de facto semantic — see Chart palette below).

| Token | Light (OKLCH) | Dark (OKLCH) | Purpose |
|---|---|---|---|
| `--background` | 1.000 0.000 0 | 0.155 0.005 285 | Page background |
| `--foreground` | 0.205 0.005 285 | 0.965 0.005 285 | Page text |
| `--card` | 1.000 0.000 0 | 0.205 0.005 285 | Card surface |
| `--card-foreground` | 0.205 0.005 285 | 0.965 0.005 285 | Card text |
| `--popover` | 1.000 0.000 0 | 0.205 0.005 285 | Popover/menu surface |
| `--popover-foreground` | 0.205 0.005 285 | 0.965 0.005 285 | Popover/menu text |
| `--primary` | 0.520 0.110 195 | 0.770 0.130 180 | Brand teal |
| `--primary-foreground` | 0.985 0.000 0 | 0.155 0.005 285 | Text on primary |
| `--secondary` | 0.965 0.005 285 | 0.270 0.005 285 | Secondary surface |
| `--secondary-foreground` | 0.205 0.005 285 | 0.965 0.005 285 | Text on secondary |
| `--muted` | 0.965 0.005 285 | 0.270 0.005 285 | Muted surface |
| `--muted-foreground` | 0.500 0.010 285 | 0.700 0.010 285 | Muted text |
| `--accent` | 0.945 0.020 195 | 0.310 0.040 195 | Accent surface (pale teal) |
| `--accent-foreground` | 0.300 0.080 195 | 0.900 0.040 195 | Text on accent |
| `--destructive` | 0.580 0.220 18 | 0.700 0.190 22 | Expense / destructive |
| `--success` | 0.620 0.180 150 | 0.720 0.170 150 | Income / positive |
| `--warning` | 0.770 0.170 80 | 0.820 0.160 80 | Warning |
| `--info` | 0.670 0.150 235 | 0.750 0.140 235 | Informational |
| `--border` | 0.910 0.005 285 | oklch(1 0 0 / 10%) | Borders |
| `--input` | 0.910 0.005 285 | oklch(1 0 0 / 15%) | Input borders |
| `--ring` | 0.520 0.110 195 | 0.770 0.130 180 | Focus ring |

### Sidebar tokens

The App Shell sidebar is themed with its own token group so that future re-skins (collapsed rail, mobile drawer) can be tuned without touching the main palette.

| Token | Purpose |
|---|---|
| `--sidebar` | Sidebar surface |
| `--sidebar-foreground` | Sidebar text |
| `--sidebar-primary` | Active item bar / accent rail (matches `--primary`) |
| `--sidebar-primary-foreground` | Text on the active rail |
| `--sidebar-accent` | Hover/selected row tint |
| `--sidebar-accent-foreground` | Text on the hover/selected row |
| `--sidebar-border` | Divider between sidebar and main content |
| `--sidebar-ring` | Focus ring inside the sidebar |
| `--sidebar-w` | Layout width custom property — `240px` expanded, `56px` when `<html>` has the `sidebar-collapsed` class |

Contrast ratios for every foreground/background pair are visible live on the `/design-system.html#/colors` page. Targets: WCAG AA (4.5:1 for body text, 3:1 for large text and UI components).

---

## Typography

**Two typefaces:**
- **Inter** (`--font-sans`) — all UI text and prose. Variable font (`@fontsource-variable/inter`).
- **IBM Plex Mono** (`--font-mono`) — currency, percentages, and dates in **tabular contexts only**. Static weight 400 only (`@fontsource/ibm-plex-mono`); see [Known limitations](#known-limitations).

`--font-heading` is also exposed (currently aliased to `--font-sans`); reserved so that headings can later diverge from body without touching every component.

**The rule for percentages:**
- In **data contexts** (KPI cards, table cells, chart axes): use mono via `<Numeric>`.
- In **prose** (sentences with embedded figures): use the surrounding font (Inter). Switching mid-sentence is jarring and harms readability.

The `<Numeric>` component (see below) is the enforcement mechanism — wrap any tabular numeric in it; never apply `font-mono` directly.

**Type scale:** Tailwind defaults — `text-xs` (12px) through `text-4xl` (36px). See the live scale at `/design-system.html#/typography`.

---

## Spacing, radius, shadow

- **Spacing:** Tailwind defaults (4px increments). No custom scale.
- **Radius:** `--radius: 0.625rem` (10px) is the base. The full scale is derived in `index.css` so retuning the base propagates everywhere:

| Utility | Multiplier | At `--radius: 0.625rem` |
|---|---|---|
| `rounded-sm` | 0.6× | 0.375rem |
| `rounded-md` | 0.8× | 0.5rem |
| `rounded-lg` | 1.0× | 0.625rem |
| `rounded-xl` | 1.4× | 0.875rem |
| `rounded-2xl` | 1.8× | 1.125rem |
| `rounded-3xl` | 2.2× | 1.375rem |
| `rounded-4xl` | 2.6× | 1.625rem |

`<Badge>` uses `rounded-4xl` for its pill shape; cards use `rounded-xl`; inputs and buttons use `rounded-md`.

- **Shadow:** four steps — `shadow-sm`, `shadow`, `shadow-md`, `shadow-lg`. Dark-mode shadows are darker because they sit on dark surfaces. All four are aliased in `@theme inline` so the Tailwind utilities pick up the custom values.

**Skeletons:** Match the rendered content's height to prevent layout shift. Common heights: `h-[220px]` for chart cards, `h-[400px]` for tables, `h-5 w-32` for individual text rows.

### Vertical rhythm

Three gap sizes cover almost every case. Pick by the *relationship* between the elements, not by how it looks in isolation.

| Class | Pixels | Use for |
|---|---|---|
| `space-y-2` | 8px | Within a related cluster — heading + description, label + value, items in a stat row |
| `space-y-6` / `py-6` | 24px | Default block-to-block on a page — filter bar → content, card → card, section → section |
| `pt-10` | 40px | After sticky/fixed chrome — pinned bars carry extra visual weight (own background, fixed position) and need more breathing room than an inline block |

**Don't stack a hard divider on top of generous whitespace.** If the element above has `border-b` and the element below has its own border (Card, table), drop the `border-b` — the background contrast plus whitespace already defines the boundary. Two thin lines with 24–40px between them reads as "two boxes pressed together," not "two distinct sections." Pick one boundary cue: divider line **or** whitespace.

This rule is why `ReportsLayout` removed the `border-b` from its sticky chrome wrapper: the outlet content below already renders Cards with their own borders, and the chrome's bg-background plus `pt-10` of whitespace is enough.

---

## Motion

| Token | Value | Use for |
|---|---|---|
| `--motion-duration-fast` | 120ms | Micro-interactions (hover, focus) |
| `--motion-duration-base` | 180ms | Standard UI transitions |
| `--motion-duration-slow` | 260ms | Larger transitions (modal in/out) |
| `--motion-easing-standard` | cubic-bezier(0.2, 0, 0, 1) | Default — feels responsive |
| `--motion-easing-emphasized` | cubic-bezier(0.3, 0, 0, 1) | When the motion needs more weight |

Components must reference these tokens via `transitionDuration: 'var(--motion-duration-base)'` rather than inline numeric durations.

Motion tokens are defined only on `:root` and intentionally not redefined on `.dark` — they don't change with theme.

> **Status of the rule (2026-05-08):** enforced — every app and showcase component uses the motion tokens. The shadcn-vendored `sheet.tsx` retains its literal because it is a vendored primitive.

### View transition naming

The SPA uses CSS view transitions to smooth row-to-form navigation in feature flows. To keep slot names coherent, follow this convention:

- **Per-row slot:** `<feature>-row-<id>` — e.g. `movement-row-${id}` on each `<tr>` in the Movements table.
- **Per-form slot:** `<feature>-form` — e.g. `movement-form` on the Movements edit form root.

A click on a row that opens the matching form cross-fades the bounding boxes; a click on a row that does *not* open a matching form falls back to the default page transition. New features should pick names following the same `<feature>-row-<id>` / `<feature>-form` shape so future grouping (e.g. shared element transitions across pages) keeps working.

---

## Chart palette

Eight qualitative colors (`--chart-1` through `--chart-8`), all WCAG AA against `--background` in both modes.

| Token | Light hue | Dark hue | Suggested use |
|---|---|---|---|
| `--chart-1` | Teal | Teal | Primary series |
| `--chart-2` | Emerald | Emerald | Income |
| `--chart-3` | Sky | Sky | Info / secondary income |
| `--chart-4` | Amber | Amber | Warning category |
| `--chart-5` | Rose | Rose | Expense |
| `--chart-6` | Violet | Violet | **Transfer** (movement-type tint — see below) |
| `--chart-7` | Orange | Orange | **Liability Payment** (movement-type tint — see below) |
| `--chart-8` | Slate-blue | Slate-blue | Neutral series / variety |

Always use the tokens via `var(--chart-N)`. Chart components should never hard-code colors.

**Semi-semantic chart hues.** `--chart-6` and `--chart-7` started as qualitative variety colors but have settled into specific roles in Movements: violet for **Transfer**, orange for **Liability Payment**. Treat them as semi-semantic — fine to reuse in *charts* as variety, but if you tint a non-Transfer / non-LiabilityPayment row with chart-6/7 in the Movements table, you'll create a false signal. Use `chart-8` (slate-blue) for additional variety series before reaching back to 6/7.

---

## shadcn/ui overrides

`components.json` is set to `style: base-nova`, `baseColor: zinc`, `cssVariables: true`. The default shadcn variables are overridden in `index.css`:

| Variable | Overridden? | Why |
|---|---|---|
| `--primary` / `--primary-foreground` | Yes | Brand teal |
| `--ring` | Yes | Match primary |
| `--accent` / `--accent-foreground` | Yes | Pale teal accent |
| `--success` / `--warning` / `--info` | Added | Not in shadcn default |
| `--sidebar*` (8 tokens) | Added | App Shell sidebar theming — see Color palette |
| `--chart-1..8` | Yes (5 from shadcn, 6–8 added) | Brand-aligned palette |
| `--shadow-*` | Added | shadcn relies on Tailwind defaults; we tune for both modes |
| `--motion-*` | Added | Not in shadcn |
| All other shadcn tokens | Left at defaults (zinc baseline) | Works with the brand |
| `Button` cursor | Yes | Default shadcn Button has no cursor override; we apply `cursor-pointer` so all interactive buttons get the hand cursor on hover |
| `Badge` variants | Extended | Added `success`, `warning`, `info` semantic variants (soft-tinted, matching the existing `destructive` recipe), plus `ghost` and `link` for low-emphasis use |
| `Switch` styling | Tuned | Custom CSS in `index.css` adapts the base-ui data attributes to the project palette |

### Primitives in `src/components/ui/`

`pnpm dlx shadcn add <component>` has been run for these — all live under `src/components/ui/` and consume the same tokens:

`alert-dialog`, `avatar`, `badge`, `button`, `calendar`, `card`, `chart`, `command`, `dialog`, `dropdown-menu`, `input`, `input-group`, `kbd`, `label`, `navbar`, `popover`, `progress`, `select`, `separator`, `sheet`, `skeleton`, `sonner`, `switch`, `table`, `tabs`, `textarea`, `tooltip`.

**Notable additions during the SPA migration:**
- **`InputGroup`** — input with prefix/suffix slots; used for the currency-symbol addon on the money input.
- **`Kbd`** — keyboard-shortcut chip (e.g. `⌘K` in the TopBar search trigger).
- **`Navbar`** — primitive used by the App Shell TopBar.
- **`AlertDialog`** — the SPA's confirmation dialog standard. The legacy `<ConfirmDialog>` (`src/components/ConfirmDialog.tsx`) is a Razor-era artefact pending removal; do not use it for new work.

### Sonner customization

The Sonner toaster is themed via `index.css` so each toast type carries an appropriately tinted icon while keeping titles at the popover-foreground color for AA contrast:

```css
[data-sonner-toast][data-type='success'] [data-icon] { color: var(--success); }
[data-sonner-toast][data-type='warning'] [data-icon] { color: var(--warning); }
[data-sonner-toast][data-type='error']   [data-icon] { color: var(--destructive); }
[data-sonner-toast][data-type='info']    [data-icon] { color: var(--info); }
```

If you add a new toast type or restyle Sonner, edit only this block — never override the toast surface or text colour at the call site.

### base-ui vs. Radix

The `base-nova` style uses `@base-ui/react` primitives, not Radix. The two have different APIs in places (e.g., the base-ui `Tooltip.Trigger` does not take an `asChild` prop). When porting shadcn snippets from elsewhere, check the primitive source under `src/components/ui/` to confirm the local API.

---

## The `<Numeric>` component

`ProjectCeres.Client/src/components/Numeric.tsx`

```tsx
<Numeric>€1.234,56</Numeric>          // span, mono, tabular-nums
<Numeric as="td">73%</Numeric>         // td inside a table row
<Numeric className="text-destructive">-€500</Numeric>
```

When **not** to use `<Numeric>`: percentages or amounts that appear inside a sentence. Example:

```tsx
// ✓ Keep prose in Inter
<p>You've spent 73% of your budget so far.</p>

// ✗ Don't switch fonts mid-sentence
<p>You've spent <Numeric>73%</Numeric> of your budget so far.</p>
```

---

## Status badges

Use shadcn `<Badge>` for any small status indicator (cleared/pending state, alert tags, etc.). The semantic variants pair a tinted background with the matching foreground:

| Variant | Background | Text | Use for |
|---|---|---|---|
| `default` | primary | primary-foreground | Brand accent (rare on data tables) |
| `secondary` | secondary | secondary-foreground | Neutral metadata, "Pending" cleared state |
| `outline` | transparent | foreground | Quiet metadata |
| `destructive` | destructive/10 | destructive | Errors, delete-confirmation tags |
| `success` | success/10 | success | Cleared, paid, positive states |
| `warning` | warning/10 | warning | "Needs review", attention-needed |
| `info` | info/10 | info | Informational tags |
| `ghost` | transparent → muted on hover | foreground | Hover-revealed actions inside dense rows |
| `link` | transparent | primary (underlined on hover) | Inline-link-styled tags |

```tsx
<Badge variant="success">Cleared</Badge>
<Badge variant="secondary">Pending</Badge>
<Badge variant="warning">Needs review</Badge>
<Badge variant="info">Transaction</Badge>
```

### Tonal chip on chart hue (named recipe)

When you need a tinted chip whose meaning is not in the semantic set above — typically a transaction-type tint — opt out of `variant` and apply the chart token directly:

```tsx
<Badge className="bg-chart-6/10 text-chart-6">Transfer</Badge>
<Badge className="bg-chart-7/10 text-chart-7">Liability Payment</Badge>
```

The pattern is `bg-chart-N/10 text-chart-N`. This is the only blessed way to step outside the semantic variants; the explicit `className` signals "this tint is a deliberate non-semantic choice." Prefer adding a new semantic variant if you find yourself reaching for this in three or more unrelated places.

For destructive operations (delete confirmations), prefer a confirmation dialog over a badge.

---

## Layout primitives

Four small components for arranging stats and data. All live under `src/components/`.

### `<StatTile>` — vertical KPI

Label on top, value below. Use for prominent metrics that deserve visual weight.

```tsx
<StatTile label="Net Worth" value={<Numeric>€ 1,234.56</Numeric>} />
```

### `<StatRow>` — inline label/value

Label on the left, value on the right (justify-between). Use inside a `<dl className="space-y-2">` for grouped stats (MTD card, breakdown lists).

```tsx
<StatRow label="Income" value={<Numeric className="text-success">€ 3,200</Numeric>} />
```

### `<EquationRow>` — compact muted caption

Smaller (`text-[11px]`) and muted by default. Use inside dense vertical stacks for breakdowns (e.g., Spendable Balance components). Pass `valueClassName` to override the muted default for a headline row.

```tsx
<EquationRow label="Liquid" value={<Numeric>€ 1,200</Numeric>} />
<EquationRow
  label="Available today"
  value={<Numeric className="text-base font-bold text-success">€ 430</Numeric>}
/>
```

### `<Tile>` — KPI surface wrapper

Muted background + rounded + padding. Use to visually group small numeric tiles (e.g., 3-up MTD breakdown).

```tsx
<Tile>
  <StatTile label="Income" value={<Numeric className="text-2xl text-success">€ 3,200</Numeric>} />
</Tile>
```

---

## App Shell

`ProjectCeres.Client/src/app/layout/`

The authenticated SPA renders inside a fixed grammar: a 3.5rem TopBar, a collapsible Sidebar (desktop) or Sheet-based drawer (mobile), and a scrollable main content area. Every routed page in `src/app/` mounts inside this shell — never break out of it.

### Anatomy

```
AppLayout.tsx     ─ 3.5rem TopBar / 1fr content grid; mounts <Toaster />
├── TopBar.tsx       ─ brand mark · search trigger (⌘K) · avatar menu
├── Sidebar.tsx      ─ desktop nav, expand/collapse toggle
├── MobileDrawer.tsx ─ Sheet-based sidebar at <640px
├── BrandMark.tsx    ─ wordmark used in TopBar and Sidebar
├── AvatarMenu.tsx   ─ shadcn DropdownMenu trigger
└── nav-items.ts     ─ single source of truth for nav routes
```

### Layout grid

`AppLayout.tsx` wraps everything in:

```tsx
<div className="grid h-screen grid-rows-[3.5rem_1fr] bg-background text-foreground">
  <TopBar … />
  <div
    className="grid overflow-hidden"
    style={{ gridTemplateColumns: isDesktop ? 'var(--sidebar-w, 240px) 1fr' : '1fr' }}
  >
    {isDesktop ? <Sidebar /> : <MobileDrawer … />}
    <main id="main-content" className="overflow-y-auto p-6"><Outlet /></main>
  </div>
  <Toaster />
</div>
```

The desktop split is driven by **`--sidebar-w`** (`240px` expanded, `56px` collapsed). The collapsed state is signalled by adding the `sidebar-collapsed` class to `<html>` — the CSS in `index.css` redefines `--sidebar-w` on that selector, which propagates to the grid through `var(--sidebar-w)`. A single click on the Sidebar toggle persists the preference via `lib/sidebar-storage.ts`.

The breakpoint between desktop and mobile is **640px** (`useMediaQuery('(min-width: 640px)')`). Below that, the Sidebar is replaced with a Sheet drawer triggered from the TopBar hamburger.

### Active-rail recipe

The current route in the Sidebar is signalled by a 2px primary-color inset bar plus an accent background:

```tsx
className={cn(
  'flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors',
  isActive
    ? 'bg-accent text-accent-foreground shadow-[inset_2px_0_0_var(--primary)]'
    : 'text-muted-foreground hover:bg-muted hover:text-foreground'
)}
```

Use `shadow-[inset_2px_0_0_var(--primary)]` (not a left border) — the inset shadow does not affect the element's box and so does not cause active links to shift right.

### TopBar search trigger

The search affordance is a Button with a `<Kbd>` chip showing the platform-correct shortcut (`⌘K` on macOS, `Ctrl+K` elsewhere):

```tsx
<Button variant="outline" onClick={() => setSearchOpen(true)} className="gap-2">
  <Search size={16} />
  <span className="text-muted-foreground">Search…</span>
  <Kbd>{isMac() ? '⌘K' : 'Ctrl+K'}</Kbd>
</Button>
```

The Kbd primitive is documented under [shadcn/ui overrides](#shadcnui-overrides). Reuse this pattern for any global keyboard shortcut surfaced in the UI.

### Skip link

`AppLayout.tsx` registers a visually hidden "Skip to main content" link as its first child. It becomes visible on focus and is the only way a keyboard user can bypass the TopBar/Sidebar to reach the routed content. Don't remove it.

---

## Page layout recipes

### Heading → filter bar → content (inline filter bar)

For pages where the filter bar lives in the normal content flow (no sticky chrome). For sticky-chrome layouts, see the next subsection.

**The correct pattern** — matches how Movements spaces its filter bar and table:

```tsx
<div className="space-y-6">          {/* 24px gap between heading-group and results */}
  <div>                               {/* plain wrapper — no gap between heading and filter bar */}
    <ReportHeader title="…" filters={filters} />
    <ReportsFilterBar />              {/* sits flush below heading */}
  </div>
  <ReportTableCard …>…</ReportTableCard>  {/* 24px below filter bar */}
</div>
```

**`ReportsFilterBar` padding:** Use `pt-3` only — **no `pb-*` or `py-*`**. Bottom padding on the wrapper creates an extra visible gap between the filter inputs and the card below, on top of the `space-y-6` gap. The result looks like a double gap. `pt-3` gives breathing room between the heading text and the filter inputs without adding space below.

**Why this keeps getting broken:** `space-y-6` on the outer div is necessary for the filter→results gap. The heading→filter flush is achieved by wrapping them in a plain `<div>` (no spacing class) so `space-y-6` treats them as one unit. Any attempt to add `mb-*`, `pb-*`, or extra wrappers between the filter bar and the results will reintroduce the double gap.

### Sticky chrome → content (Reports SPA pattern)

When tabs, header, and filters are pinned together at the top of the scroll container (see `ReportsLayout`), the rhythm shifts:

- **Chrome wrapper:** `sticky top-0 z-10` plus a bleed (`-mx-6 -mt-6`) so the chrome can extend full-width while the inner content keeps the same horizontal indent as the body. Re-indent inside the bleed with `mx-6` so `px-[8%]` calculations match across the chrome and the outlet.
- **No `border-b` on the chrome wrapper.** The bg-background of the chrome plus the gap below is enough to separate it from page content. Adding a divider creates the double-boundary problem (see Vertical rhythm).
- **Outlet wrapper:** `pt-10 pb-6` — 40px above to give content breathing room from the pinned chrome, 24px below for normal page-bottom rhythm.
- **All filter bars (shared and per-page) live inside the chrome,** not in the page body. If only some filters are pinned and others scroll, the layout reads as inconsistent.

---

## Forms & validation

Every form in the SPA renders against the project's 422 validation envelope — `{ error: { code: "VALIDATION_ERROR", details: [{ field, message }] } }`. Two pieces work together: a **Field wrapper** that pairs a label with its inline error, and a normalisation step that flattens the envelope into a `Record<string, string>` keyed by camelCase field name.

### `<Field>` primitive

`<Field>` (`src/app/components/Field.tsx`) is the single label + control + inline-error wrapper used by every form in the SPA. Three local copies (QuickAddModal, MovementForm, RecurringForm) collapsed into this primitive in commit `fa6c0d7`.

```tsx
<Field label="Description" htmlFor="mf-desc" error={errors.description}>
  <Input id="mf-desc" value={description} onChange={(e) => setDescription(e.target.value)} />
</Field>

<Field label="Account" error={errors.accountId}>
  <AccountCombobox accounts={accounts} value={accountId} onChange={setAccountId} />
</Field>
```

Use `<Field htmlFor>` when the control accepts an `id` (Input, Textarea, native Select, MoneyInput). Omit `htmlFor` for composite controls without a single focusable target (Combobox, DatePickerField — they own their own focus); the label renders as styled text instead of `<Label htmlFor>` (avoids the "no associated control" a11y warning). The `error` slot renders an inline `<p class="text-xs text-destructive">` below the control when set.

### Form-level error banner

For errors that are not bound to a specific field — submit failures, optimistic-locking conflicts, server-side cross-field issues — use a banner above the form actions:

```tsx
{error && (
  <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
    {error}
  </div>
)}
```

The pairing is: per-field errors render inline next to the field; everything else renders in the banner. **Never** route a 422 (validation) failure to the banner if you have a field key — that hides the actionable detail under a generic message.

### Normalising the 422 envelope

The server returns `details` keyed by PascalCase field names matching the ViewModel. The SPA renders against camelCase. Flatten the envelope before passing to your `<Field>` rendering:

```tsx
function flattenErrors(envelope: { error: { details: { field: string; message: string }[] } }) {
  return Object.fromEntries(
    envelope.error.details.map((d) => [
      d.field.charAt(0).toLowerCase() + d.field.slice(1),
      d.message,
    ])
  );
}

const errors = flattenErrors(await response.json());
// → { amount: "Must be greater than 0.", date: "Date is required." }
```

Then `errors.amount`, `errors.date`, etc. plug straight into the `<Field error>` prop.

### When *not* to toast

See the [422 carve-out](#the-422-carve-out--never-toast-a-validation-failure) under Toasts. Validation failures are inline-only.

---

## Combobox recipe

`ProjectCeres.Client/src/app/components/AccountCombobox.tsx`
`ProjectCeres.Client/src/app/components/CategoryCombobox.tsx`
`ProjectCeres.Client/src/components/CurrencyCombobox.tsx`

The canonical "searchable dropdown" recipe: shadcn `<Popover>` wrapping a `<Command>` palette, triggered by a full-width outline `<Button>` with a chevron affordance. Used three times today — Account, Category, Currency — and the next "pick one of N" flow should reuse this exact shape.

```tsx
<Popover open={open} onOpenChange={setOpen}>
  <PopoverTrigger
    render={
      <Button variant="outline" role="combobox" aria-expanded={open} className="w-full justify-between">
        {selected ? selected.name : <span className="text-muted-foreground">{placeholder}</span>}
        <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
      </Button>
    }
  />
  <PopoverContent className="p-0" align="start">
    <Command>
      <CommandInput placeholder="Search accounts…" />
      <CommandList>
        <CommandEmpty>No accounts found.</CommandEmpty>
        <CommandGroup>
          {filtered.map((item) => (
            <CommandItem key={item.id} value={item.name} onSelect={() => { onChange(item.id); setOpen(false); }}>
              <Check className={cn('mr-2 h-4 w-4', value === item.id ? 'opacity-100' : 'opacity-0')} />
              {item.name}
            </CommandItem>
          ))}
        </CommandGroup>
      </CommandList>
    </Command>
  </PopoverContent>
</Popover>
```

**Conventions:**
- Trigger button is `variant="outline"` with `role="combobox"` and `aria-expanded`.
- Placeholder is rendered in `text-muted-foreground` so the unselected state is clearly distinct from a selected value.
- Always include a `CommandEmpty` fallback ("No X found.") — never let the list be silently empty.
- The `Check` icon is rendered for every row with `opacity-100` on the selected item and `opacity-0` otherwise (not conditionally rendered) — keeps row heights stable.
- Right-aligned metadata (e.g. CategoryCombobox shows the category type) goes in a `text-muted-foreground` `<span>` after the label.

**Inline clear (`onClear`):** opt-in via `onClear?: () => void`. When passed AND a value is selected, the trigger renders a small ✕ button next to the chevron; clicking calls `onClear()` and stops propagation so the popover stays closed. Use it on filter bars (delete the URL param) and on form fields (set state to null/`''`). Omit on surfaces where clearing doesn't make sense.

**When to deviate:** if you need multi-select, use shadcn `<Command>` checkbox patterns rather than this recipe. If you need a non-searchable picker for a small fixed set (≤5 options), prefer a native `<Select>` or `<RadioGroup>`.

---

## DatePickerField

`ProjectCeres.Client/src/components/DatePickerField.tsx`

The canonical date input. A shadcn `<Popover>` triggered by an outline `<Button>` with a calendar icon, opening to a `<Calendar mode="single">`. Used by every form that captures a date — `MovementForm`, `QuickAddModal`, `MovementsFilterBar`, `GoalBudgetForm`.

```tsx
<DatePickerField
  id="mf-date"
  value={values.date}              // ISO yyyy-MM-dd or null
  onChange={(d) => set('date', d)} // null when cleared
  placeholder="Pick a date"
/>
```

**Props:**

- `value: string | null` — ISO `yyyy-MM-dd`, or `null` for empty.
- `onChange: (next: string | null) => void` — receives ISO string on pick, `null` on clear.
- `placeholder?: string` — defaults to `'Pick a date'`.
- `id?: string` — forwarded to the trigger button so a `<Label htmlFor>` can target it.
- `hideClear?: boolean` — set when the field is required, to suppress the in-popover Clear button.

### Conventions

- **Input/output is always ISO `yyyy-MM-dd`.** The component owns the timezone-safe conversion to/from `Date`. Don't pass JS `Date` objects through this component.
- **Display format is locale-driven.** The trigger label calls `formatDate(value, dateFormat)` where `dateFormat` comes from `useSettings()` — a user with EU settings sees `02/05/2026`, a US user sees `05/02/2026`, both for the same ISO string `2026-05-02`.
- **Empty state.** When `value` is `null`, the trigger renders the placeholder in `text-muted-foreground` so the unselected state matches the Combobox recipe.
- **Clear footer.** Visible only when there is a value AND `hideClear` is not set. Renders inside a `border-t p-2` block so it visually separates from the calendar grid.

### When *not* to use `DatePickerField`

- **Date *ranges*** — use `<DateRangePicker>` (`src/components/DateRangePicker.tsx`) for the generic from/to range used by the Reports filter bar, or `<MovementsDateRangePicker>` for the Movements-specific paired-input pattern. `<DatePickerField>` is single-day only.
- **Time-of-day** — out of scope; the underlying value is `DateOnly` on the server.
- **Read-only display of an existing date** — render `formatDate(value, dateFormat)` directly inside a `<Numeric>` cell or plain text. Don't disable the picker.

---

## Money input

`<MoneyInput>` (`src/app/components/MoneyInput.tsx`) is the single primitive for money/currency entry. It encapsulates the raw/display/wire model and the locale-aware sanitize/format helpers. Every monetary field in the SPA uses it.

### The raw / display / wire model

A money field has three string forms, defined in `src/app/lib/amount-format.ts`:

| Form | Example | When used |
|---|---|---|
| **raw** | `1234,56` | While the field is focused — digits + the user's decimal separator only |
| **display** | `1.234,56` | While the field is blurred — raw with thousands separators inserted |
| **wire** | `1234.56` | Crossing the network — JS number, period decimal |

The user's chosen number format (stored in Settings) decides which separator is which: `comma_decimal` ⇒ `.` thousands / `,` decimal; `period_decimal` ⇒ `,` thousands / `.` decimal. `<MoneyInput>` does the conversion internally; consumers only ever see the wire format.

### Usage

```tsx
<Field label="Amount" htmlFor="my-amount" error={errors.amount}>
  <MoneyInput
    id="my-amount"
    value={values.amount}            // wire format ("123.45")
    onChange={(wire) => set('amount', wire)}
    currencySymbol={selectedAccount?.currencySymbol}
  />
</Field>
```

The `id` prop is required and forwarded onto the underlying input — pair with `<Field htmlFor={...}>` so the label/control association is correct. The component renders a Skeleton (carrying the same id) while `useSettings` resolves, so the htmlFor stays valid throughout.

### Conventions enforced by the component

- **`type="text"` with `inputMode="decimal"`**, never `type="number"`. `type="number"` strips trailing zeros, doesn't honour locale separators, and exposes a useless spinner control. `inputMode="decimal"` brings up the right mobile keyboard.
- **Keystrokes are filtered** through `sanitizeAmountInput` — drops letters, normalises a wrongly-typed separator, blocks a second separator. Don't validate format; refuse the bad keystroke.
- **Display formatted on blur, raw on focus.** Thousands separators strip on focus, re-apply on blur. The user types digits + decimal; commas/periods appear when they leave the field.
- **Currency symbol via `<InputGroupAddon align="inline-start">`.** Pass `currencySymbol` and the prefix renders in the addon (not part of the input string). The addon's text style is `text-muted-foreground` so it doesn't compete with the typed amount.
- **Wire format on the boundary.** `value` and `onChange` always speak wire format. Submit `Number(values.amount)`; reverse with `formatNumberForDisplay` only when *displaying* a number outside an input (KPI tiles, table cells).

### When *not* to use `<MoneyInput>`

- **Read-only money display** (KPI tiles, table cells, summary rows) — use `<Numeric>` with the value pre-formatted via `formatNumberForDisplay`. The input recipe is for *entry*, not *display*.
- **Budget percentages, savings rates** — those are unitless ratios; render them directly as `${(fraction * 100).toFixed(1)}%`. `<MoneyInput>` is currency-specific.

---

## Delta / PriorComparison row

`ProjectCeres.Client/src/app/features/dashboard/MtdCard.tsx` (the `PriorComparison` helper, currently inlined).

The dashboard's "vs last period" caption: an arrow icon plus a colour-coded delta line, with a polarity flag controlling whether *up* means *good* or *bad*. The pattern is generic enough to belong outside the MTD card — promote to a shared component when the second consumer arrives.

```tsx
<PriorComparison
  current={data.mtd.income}
  prior={data.mtd.priorPeriodIncome}
  formatValue={(n) => formatMoney(n, sym, fmt)}
  goodWhenUp={true}
/>
```

### Polarity — the `goodWhenUp` flag

Every metric has an implied "is more better?" answer:

| Metric | `goodWhenUp` | Up colour | Down colour |
|---|---|---|---|
| Income | `true` | `text-success` | `text-destructive` |
| Savings Rate | `true` | `text-success` | `text-destructive` |
| Expenses | `false` | `text-destructive` | `text-success` |
| Net Flow | depends — pass `true` if positive net flow is the goal | — | — |

Always be explicit: pass `goodWhenUp` rather than letting the component guess from the metric name. A future "Days Until Payday" metric is `goodWhenUp={false}` (lower is better), and the rule generalises.

### States

- **`prior === null`** — render the muted *"No prior period to compare"* line. Don't render an arrow or a colour — there's nothing to compare against.
- **`current === prior`** (flat) — render `Minus` icon in `text-muted-foreground`. Flat is neither good nor bad.
- **`current > prior`** — `ArrowUp` icon, colour decided by `goodWhenUp`.
- **`current < prior`** — `ArrowDown` icon, colour decided by `goodWhenUp`.

### Anatomy

```
mt-1                          ← 4px gap below the value above
flex items-center gap-1
text-xs                        ← always xs — this is a caption, not a value
text-{success|destructive|muted-foreground}

  <Icon className="h-3 w-3" aria-hidden />
  <span>vs €1,234.56 last period</span>
```

The `vs` prefix and `last period` suffix are part of the recipe — don't substitute "vs prior" or "last month" without a reason. The phrasing is calibrated against the dashboard's "Cycle to Date" framing.

### When *not* to use this row

- **For absolute change** (`+€500 from last period`) where the user does not need the prior value itself — render a single `<Numeric>` with sign + colour, no arrow row.
- **For trends across multiple periods** — use a sparkline / chart, not a single delta caption.

---

## Tooltipped panel labels & empty states

`ProjectCeres.Client/src/app/features/dashboard/FinancialHealthCard.tsx` (the `PanelLabel` and `PanelEmpty` helpers, currently inlined).

The Financial Health card composes four equal-width panels — Spendable, Runway, Income Δ, Burn Rate — each with the same uppercase tracked label and the same two empty-state styles. The visual recipe is generic; promote to a shared component when a non-Health surface adopts it.

### Panel label

An uppercase, tracked, muted heading with an optional `?` info-tooltip:

```tsx
<PanelLabel tooltip="What's free to spend right now after upcoming bills.">
  Spendable Balance
</PanelLabel>
```

```tsx
function PanelLabel({ children, tooltip }: { children: string; tooltip?: string }) {
  return (
    <div className="text-xs uppercase tracking-wider text-muted-foreground font-medium mb-2 flex items-center gap-1.5">
      <span>{children}</span>
      {tooltip && (
        <TooltipProvider delay={200}>
          <Tooltip>
            <TooltipTrigger
              render={
                <button
                  type="button"
                  aria-label={`About ${children}`}
                  className="text-muted-foreground hover:text-foreground transition-colors"
                >
                  <Info className="h-3 w-3" />
                </button>
              }
            />
            <TooltipContent className="max-w-xs">{tooltip}</TooltipContent>
          </Tooltip>
        </TooltipProvider>
      )}
    </div>
  );
}
```

**Conventions:**

- Class shape is fixed: `text-xs uppercase tracking-wider text-muted-foreground font-medium`. Don't substitute `text-sm` or drop the tracking — every panel label across the dashboard uses this exact recipe.
- The Info button must be a real `<button>` with `aria-label="About {label}"`, not a styled `<span>`. Tooltips are not keyboard-reachable when triggered by non-button elements.
- Tooltip content is capped at `max-w-xs` (320px) and uses `<TooltipProvider delay={200}>` to avoid flashing on hover-through.
- The `?` glyph is `<Info className="h-3 w-3" />` from lucide-react — do not swap for a `(?)` text character or a different lucide icon.

### Panel empty states

Two distinct shapes — pick by *information density*, not aesthetics:

**Visual block** (icon + caption, centred, fills the panel):

```tsx
<PanelEmpty icon={<Wallet className="h-6 w-6" />}>
  No asset accounts found
</PanelEmpty>
```

Used when the panel would otherwise be a large blank — gives the eye somewhere to land. Min-height is `100px` so the panel doesn't collapse.

**Compact note** (italic muted text, inline):

```tsx
<PanelEmpty>Not enough history yet</PanelEmpty>
```

Used when the surrounding panel still has other content (a label, secondary captions) and the empty state is more like a side note than a missing block.

```tsx
function PanelEmpty({ icon, children }: { icon?: React.ReactNode; children: string }) {
  if (icon) {
    return (
      <div className="flex flex-col items-center justify-center gap-2 min-h-[100px] py-2 text-center">
        <div className="text-muted-foreground" aria-hidden="true">{icon}</div>
        <p className="text-xs text-muted-foreground max-w-[14rem]">{children}</p>
      </div>
    );
  }
  return <div className="text-xs italic text-muted-foreground">{children}</div>;
}
```

**Decision rule:** if the panel has *no other content* in the empty state, use the visual block. If the empty state replaces only one sub-section, use the compact note.

### When *not* to use these

- **Card-level errors** — use `<CardError>`, not `PanelEmpty`. Errors are recoverable; empty states are not.
- **Loading** — use `<Skeleton>` matching the loaded content's height, not an empty state.

---

## PagePlaceholder & route-change focus

`ProjectCeres.Client/src/app/components/PagePlaceholder.tsx`

The standard "this route exists but its real content lands in a later plan" surface. Used by every routed page that hasn't been implemented yet (Recurring, Reports, Profile, Security, Support).

```tsx
<PagePlaceholder
  title="Reports"
  description="This page will let you build reusable reports across your transactions."
/>
```

**Props:** `title: string`, `description: string`. That's it — keep the surface deliberately uniform across placeholder routes so they read as "not yet" rather than "broken."

### The route-change focus convention

`PagePlaceholder` does one thing beyond the visible card: it focuses its `<h1>` on mount.

```tsx
const headingRef = useRef<HTMLHeadingElement>(null);
useEffect(() => {
  headingRef.current?.focus();
}, []);

return (
  <h1 ref={headingRef} tabIndex={-1} className="text-2xl font-semibold outline-none">
    {title}
  </h1>
);
```

`tabIndex={-1}` makes a non-interactive heading programmatically focusable; `outline-none` suppresses the focus ring (the visible focus signal is the route change itself, not a ring on the heading). Screen-reader users hear the new heading announced when the focus lands — without this, the SPA route change is silent and the user has to re-explore the page to find the new content.

**The convention generalises.** Every routed page should focus its `<h1>` on mount, the same way. If you wire a new route up and the page already has a heading, mirror the `useRef` + `useEffect(() => headingRef.current?.focus(), [])` pattern. Skip it only when the route change immediately moves focus elsewhere by user intent (e.g. opens a modal in the routed page's first paint).

---

## Status block (success / idle side panel)

`ProjectCeres.Client/src/app/features/movements/MovementForm.tsx` (the "Status row" block, currently inlined).

A reusable side-panel recipe for *binary state with affordance*: an icon + heading + caption block whose colours swap when a Switch flips. Used today for the **Cleared** toggle on a transaction; will fit equally well for "Reconciled?", "Confirmed?", "Active?" surfaces in future features.

```tsx
<div
  className={
    'flex items-start justify-between gap-4 rounded-md border p-4 transition-colors [transition-duration:var(--motion-duration-base)] ' +
    (values.isCleared
      ? 'border-success/30 bg-success/10'
      : 'border-border bg-muted/30')
  }
>
  <div className="flex items-start gap-3">
    <div
      className={
        'mt-0.5 transition-colors [transition-duration:var(--motion-duration-base)] ' +
        (values.isCleared ? 'text-success' : 'text-muted-foreground')
      }
      aria-hidden="true"
    >
      {values.isCleared ? <CheckCircle2 className="h-5 w-5" /> : <Clock className="h-5 w-5" />}
    </div>
    <div className="space-y-0.5">
      <div
        className={
          'text-sm font-medium tracking-wide transition-colors [transition-duration:var(--motion-duration-base)] ' +
          (values.isCleared ? 'text-success' : 'text-foreground/80')
        }
      >
        Status
      </div>
      <p
        className={
          'text-xs transition-colors [transition-duration:var(--motion-duration-base)] ' +
          (values.isCleared ? 'text-success/80' : 'text-muted-foreground')
        }
      >
        {values.isCleared ? 'Cleared the bank.' : "Hasn't cleared the bank yet."}
      </p>
    </div>
  </div>
  <Switch
    checked={values.isCleared}
    onCheckedChange={(checked) => set('isCleared', checked)}
    aria-label="Cleared"
  />
</div>
```

### Conventions

- **Colour pairing.** Active state uses `border-{semantic}/30 bg-{semantic}/10` for the surface and `text-{semantic}` for the icon and heading; idle state uses `border-border bg-muted/30` for the surface and `text-muted-foreground` / `text-foreground/80` for the text. Stick to this pairing — don't mix a primary surface with a destructive caption, etc.
- **Icon swaps with state.** Pick two icons that signal the same axis (`CheckCircle2` ↔ `Clock` for done/not-done; could equally be `Lock` ↔ `Unlock` for sealed/open).
- **Two text rows.** The bold heading is fixed-text ("Status", "Reconciliation", etc.). The caption changes wording with state, in plain past-tense for the "done" case ("Cleared the bank.") and present-imperfect for the "not yet" case ("Hasn't cleared the bank yet.").
- **Affordance on the right.** A `<Switch>` lives flush-right; it's the only interactive thing in the block. Don't pair this recipe with a button — the affordance is *settings-like*, not *action-like*.
- **`transition-colors [transition-duration:var(--motion-duration-base)]`** uses the motion-base token directly. New components should follow the same pattern.

### When *not* to use this

- **Three-state toggles** (e.g. Cleared / Pending / Needs review) — use a `<Badge>` or a segmented control; the binary surface-and-text swap won't carry the third state cleanly.
- **Required fields** — this is a *settings* affordance, not a validation one. If the field must be answered before save, a checkbox with an error message is the right shape.

---

## AttachmentDropzone

`ProjectCeres.Client/src/app/features/movements/AttachmentDropzone.tsx`

The project's reusable file-upload surface. A dashed-border drop area with a focused-on-hover tinted state, an upload icon, a "Choose files" button (the click fallback for the hidden `<input type="file">`), a per-file list with delete buttons, and an `<AlertDialog>` confirmation flow for deletions.

The visual recipe applies to any file-upload affordance — the CSV import flow already echoes the look. Reuse this component, or rebuild the look following the conventions below.

### Recipe

```tsx
<div
  onDragOver={(e) => { e.preventDefault(); setIsDragging(true); }}
  onDragLeave={(e) => { e.preventDefault(); setIsDragging(false); }}
  onDrop={(e) => { e.preventDefault(); setIsDragging(false); handleFiles(e.dataTransfer?.files); }}
  className={[
    'flex flex-col items-center justify-center gap-2 rounded-md border-2 border-dashed px-4 py-6 text-center transition-colors',
    isDragging ? 'border-primary bg-primary/5' : 'border-muted-foreground/30',
  ].join(' ')}
>
  <Upload className="h-5 w-5 text-muted-foreground" aria-hidden="true" />
  <p className="text-sm text-muted-foreground">Drag &amp; drop files here, or</p>
  <Button type="button" variant="outline" size="sm" onClick={() => fileInputRef.current?.click()}>
    Choose files
  </Button>
  <input ref={fileInputRef} type="file" multiple className="hidden" onChange={…} />
</div>
```

### Conventions

- **Dashed border, never solid.** A solid border reads as a static panel; the dashed border is the universal "this is a drop target" affordance.
- **Drag-state tint is `border-primary bg-primary/5`.** Subtle — not a heavy fill — so a grazing dragover doesn't flash bright.
- **Idle border is `border-muted-foreground/30`.** The lighter tone reads as "available but quiet."
- **Hidden native input + visible button.** The native `<input type="file">` is `className="hidden"` (and `tabIndex={-1}`); the visible affordance is a `Button variant="outline" size="sm"`. The button's `onClick` calls `fileInputRef.current?.click()`. Don't expose the native control directly — its default styling is platform-specific and ugly.
- **Reset the input value after handling files.** `e.target.value = ''` after `handleFiles(e.target.files)` so the user can re-pick the same file (otherwise the `change` event won't fire on the second click).
- **Pluralise the inline progress copy.** `Uploading {n} {n === 1 ? 'file' : 'files'}…` rendered in a `border-dashed border-primary/50 bg-primary/5 text-primary` banner above the dropzone. The dashed border on the banner echoes the dropzone's affordance.

### Per-file list

```tsx
<ul className="divide-y rounded-md border max-h-72 overflow-y-auto">
  {attachments.map((a) => (
    <li className="flex items-center justify-between gap-3 px-3 py-2 text-sm">
      <div className="min-w-0 flex-1">
        <div className="truncate font-medium">{a.fileName}</div>
        <div className="text-xs text-muted-foreground">
          {formatSize(a.sizeBytes)}{uploadedAt ? ` · ${uploadedAt}` : ''}
        </div>
      </div>
      <Button
        variant="ghost"
        size="icon"
        aria-label={`Delete attachment ${a.fileName}`}
        onClick={() => setConfirmDeleteId(a.id)}
        className="text-muted-foreground hover:bg-destructive/10 hover:text-destructive"
      >
        <X className="h-4 w-4" />
      </Button>
    </li>
  ))}
</ul>
```

**Conventions:**

- Filename uses `truncate` so long names ellipsis instead of wrapping; metadata sits below in `text-xs text-muted-foreground` separated by `·`.
- The delete button starts as a quiet ghost icon (`text-muted-foreground`) and tints destructive *only on hover* (`hover:bg-destructive/10 hover:text-destructive`). This is the standard "destructive action lives quietly until you hover it" pattern.
- Confirmation goes through shadcn `<AlertDialog>` — never a `confirm()` or a toast-and-undo. See [shadcn/ui overrides](#shadcnui-overrides).

### Toast on completion

After an auto-upload finishes, fire a single `toast.success('Attachment uploaded.')`. For partial-success batches (some files uploaded, some failed), fire one `toast.warning('Some attachments failed to upload.')` summarising — never one toast per file. See [Toasts](#toasts).

### When *not* to use this surface

- **Single-file picker for a one-off action** (e.g. CSV column-mapping preview) — a plain `<Button>` triggering the hidden input is enough; the dashed-border affordance is overkill for non-multi non-persistent uploads.
- **Inline avatar/profile-picture upload** — use a focused circular dropzone with a preview, not the rectangular drop area.

---

## The `<CardError>` component

`ProjectCeres.Client/src/app/components/CardError.tsx`

Standard error+retry UI for any card whose data fetch fails. Use inside `<CardContent>` when the `useApi` hook returns an error.

```tsx
{error && <CardError section="Net Worth Over Time" onRetry={refetch} />}
```

**Props:**

- `section: string` — the noun used in the message ("Couldn't load Net Worth Over Time.").
- `onRetry: () => void` — typically the `refetch` returned by `useApi`.

The component renders a muted error line with an icon, plus an outline-variant Retry button. It does not re-fetch on its own; wire `onRetry` to your data hook.

---

## Toasts

The SPA uses [Sonner](https://sonner.emilkowal.ski/) for toast notifications. A single `<Toaster />` is mounted in `AppLayout.tsx` — anywhere in the app, import `toast` from `sonner` and call:

- `toast.success('Saved.')` for successful operations
- `toast.error("Couldn't update status.")` for failures
- `toast.warning('Some attachments failed to upload.')` for partial / soft failures
- `toast.info('Sync queued.')` for ambient status updates

Each type carries a tinted icon via the per-type CSS in `index.css` (see [shadcn/ui overrides](#shadcnui-overrides)).

Recommended message style: short, sentence-case, ends in a period. Past tense for completed actions ("Saved."), contraction-friendly for failures ("Couldn't save.").

### The 422 carve-out — never toast a validation failure

When the server returns **422 Unprocessable Entity** with the project's validation envelope (`{ error: { code: "VALIDATION_ERROR", details: [{ field, message }] } }`), do NOT raise a toast. Render each `details[].message` inline next to the field that caused it:

```tsx
<Field>
  <Label>Amount</Label>
  <Input … />
  {errors.amount && <p className="text-xs text-destructive">{errors.amount}</p>}
</Field>
```

Reason: a toast that says "Validation failed" tells the user nothing they can act on, and the correction is already keyed off the field they were editing. Toasts are for events that don't have a screen position to anchor to (a save that happened, an upload that failed in the background). See [Forms & validation](#forms--validation).

Other carve-outs:
- **Partial-success uploads** (some files succeeded, some failed): a single `toast.warning(...)` summarising the failure, not one toast per failed file.
- **Auto-saves on field blur**: silent on success, `toast.error(...)` on failure.

For destructive operations, prefer a confirmation dialog over a toast.

---

## Reports index — per-report icon convention

Each report card on `/reports` carries a distinct lucide icon. The mapping is defined in `REPORT_META` inside `reports-api.ts` — add or change an icon there, not in the rendering component.

| Slug | Icon |
|---|---|
| `net-worth` | `Wallet` |
| `net-worth-over-time` | `LineChart` |
| `income-expense` | `Scale` |
| `monthly-cash-flow` | `CalendarClock` |
| `expense-breakdown` | `PieChart` |
| `budget-vs-actual` | `Target` |
| `largest-expenses` | `TrendingUp` |
| `transaction-history` | `Receipt` |

Icons are rendered as `<entry.icon className="h-6 w-6 text-primary" aria-hidden="true" />`. All icons are aria-hidden because the card label makes the icon's meaning redundant for screen readers.

---

## Showcase route

`http://localhost:5173/design-system.html` (dev) — renders every token and shadcn primitive in every state, with light/dark toggle. Open it whenever a token changes; visual regressions show up here first.

The showcase mounts a separate React entry (`src/design-system/main.tsx`) and uses `HashRouter` so navigation works on a file-based route without server cooperation. It is isolated from the production-bound `src/main.tsx` Razor-island setup.

---

## Known browser console messages

Browsing the showcase or the SPA, you may see these — none are bugs in our code:

- **`SES Removing unpermitted intrinsics`** (lockdown-install.js). Comes from a wallet/web3 browser extension (typically MetaMask) that injects SES into every page. Verify by opening in incognito with extensions disabled — the warning disappears.
- **`WebSocket connection to 'ws://localhost:7081/?token=…' failed` + `[vite] failed to connect to websocket`.** Caused by opening the showcase via the ASP.NET Core backend URL (`https://localhost:7081/design-system.html`) instead of the Vite dev server. The page renders correctly, but HMR is disabled. Fix: open `http://localhost:5173/design-system.html` (run `pnpm dev` from `ProjectCeres.Client/` first).
- **`Recharts: width(-1) and height(-1) of chart should be greater than 0`** in older builds. Fixed by adding `minWidth={0}` to all `<ResponsiveContainer>` instances; if you re-introduce this warning, set `minWidth={0}` on the new chart's container.

---

## Known limitations

These are accepted trade-offs in the current foundation. Track here so future work can revisit when needed.

- **IBM Plex Mono is the static (400) package** (`@fontsource/ibm-plex-mono`), not the variable family. fontsource does not publish a variable build of IBM Plex Mono. Bold weights (`font-bold`, `font-semibold`) on `<Numeric>` will trigger browser-synthesized bold rather than a true drawn glyph. If a future feature needs real bold numerics, add `@fontsource/ibm-plex-mono/600.css` (or 700.css) to `index.css` alongside the existing import.

- **`--warning` (amber) is at L=0.770 in light mode**, ~3:1 contrast against white. Sufficient for borders, background fills, and icons (WCAG AA Large), but **insufficient for body text** (AA requires 4.5:1). Don't use `text-warning` for inline warning labels in 16px copy. Use it for icon strokes, badge fills, alert backgrounds.

- **No paired foreground tokens for semantic colors yet.** `--success`, `--warning`, `--info` exist but `--success-foreground`, `--warning-foreground`, `--info-foreground` don't. Add them in the plan that introduces alerts, toasts, or semantic badges (likely the App Shell or Movements/Transactions plan) — they're not needed by the current design-system foundation alone.

- **Chart-3 (sky, hue 235°) and chart-8 (slate-blue, hue 240°) are perceptually close.** Acceptable while no chart uses 8 series. Revisit the chart-8 hue (e.g., shift to indigo around 270° or pink around 315°) before any chart needs all eight.

- **IBM Plex Mono default subsets include cyrillic and vietnamese.** Adds ~50KB of font files unused in our locales (en/es). Defer until bundle size becomes an issue; the fix is to import specific subsets explicitly rather than the package default.

- **Showcase contrast ratios are not displayed when tokens are defined as `oklch()`.** `getComputedStyle()` returns `oklch(...)` strings for CSS custom properties whose source value is OKLCH, and the WCAG contrast helper currently only parses `rgb()` / `rgba()`. The Colors and Charts swatches still render correctly (the page no longer crashes, and the rgb display row shows whatever the browser returned), but the "vs --foreground: X.XX : 1 (AA)" line is suppressed. Fix path: extend `parseRgb()` in `src/design-system/lib/contrast.ts` to handle OKLCH (either via `culori`/`colorjs.io` or a small inline OKLCH→sRGB conversion).

- ✅ **Motion tokens are now enforced everywhere** (migrated 2026-05-08). All app and showcase components use `[transition-duration:var(--motion-duration-base)]`. The shadcn-vendored `sheet.tsx` retains its literal because it is a vendored primitive — do not migrate it. Don't introduce new literals in app code.

- **`<ConfirmDialog>` legacy primitive.** `src/components/ConfirmDialog.tsx` is a pre-SPA artefact that submits via a hidden form id. The SPA standard is shadcn `<AlertDialog>` used inline at the call site (see `AttachmentDropzone.tsx`, `MovementForm.tsx`). Don't add new consumers; the existing ones will be migrated.
