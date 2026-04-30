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
11. [The `<CardError>` component](#the-carderror-component)
12. [Toasts](#toasts)
13. [Showcase route](#showcase-route)
14. [Known browser console messages](#known-browser-console-messages)
15. [Known limitations](#known-limitations)

---

## How to use this document

Every visual decision lives as a CSS custom property in `ProjectCeres.Client/src/index.css`. Tailwind v4 reads these via the `@theme inline` block and exposes them as utilities (`bg-primary`, `text-muted-foreground`, etc.). Components must use the utilities, never hex literals.

To retune a color or any other token, edit `index.css` only — no component changes required.

The internal `/design-system.html` route renders every token live and doubles as a visual regression check. Open it during local development to verify any token change.

---

## Color palette

The palette is defined in OKLCH for perceptual uniformity. Values land in two blocks: `:root` (light) and `.dark` (dark).

**Brand intent:**
- **Neutrals:** Zinc — cool, slightly warmer than slate; reads as calm/professional.
- **Primary:** Deep teal — distinctive (not the typical SaaS blue), reads as money/calm without being literal green.
- **Success:** Emerald — used for income, positive deltas, "cleared" states.
- **Destructive:** Rose — used for expense, delete actions, validation errors.
- **Warning:** Amber.
- **Info:** Sky.

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
| `--border` | 0.910 0.005 285 | rgba(255,255,255,0.10) | Borders |
| `--input` | 0.910 0.005 285 | rgba(255,255,255,0.15) | Input borders |
| `--ring` | 0.520 0.110 195 | 0.770 0.130 180 | Focus ring |

Contrast ratios for every foreground/background pair are visible live on the `/design-system.html#/colors` page. Targets: WCAG AA (4.5:1 for body text, 3:1 for large text and UI components).

---

## Typography

**Two typefaces:**
- **Inter** (`--font-sans`) — all UI text and prose. Variable font (`@fontsource-variable/inter`).
- **IBM Plex Mono** (`--font-mono`) — currency, percentages, and dates in **tabular contexts only**. Static weight 400 only (`@fontsource/ibm-plex-mono`); see [Known limitations](#known-limitations).

**The rule for percentages:**
- In **data contexts** (KPI cards, table cells, chart axes): use mono via `<Numeric>`.
- In **prose** (sentences with embedded figures): use the surrounding font (Inter). Switching mid-sentence is jarring and harms readability.

The `<Numeric>` component (see below) is the enforcement mechanism — wrap any tabular numeric in it; never apply `font-mono` directly.

**Type scale:** Tailwind defaults — `text-xs` (12px) through `text-4xl` (36px). See the live scale at `/design-system.html#/typography`.

---

## Spacing, radius, shadow

- **Spacing:** Tailwind defaults (4px increments). No custom scale.
- **Radius:** `--radius: 0.625rem` (10px) is the base. Tailwind's `rounded-sm/md/lg/xl/2xl/3xl/4xl` derive from it.
- **Shadow:** four steps — `shadow-sm`, `shadow`, `shadow-md`, `shadow-lg`. Dark-mode shadows are darker because they sit on dark surfaces. All four are aliased in `@theme inline` so the Tailwind utilities pick up the custom values.

**Skeletons:** Match the rendered content's height to prevent layout shift. Common heights: `h-[220px]` for chart cards, `h-[400px]` for tables, `h-5 w-32` for individual text rows.

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
| `--chart-6` | Violet | Violet | Variety |
| `--chart-7` | Orange | Orange | Variety |
| `--chart-8` | Slate-blue | Slate-blue | Neutral series |

Always use the tokens via `var(--chart-N)`. Chart components should never hard-code colors.

---

## shadcn/ui overrides

`components.json` is set to `style: base-nova`, `baseColor: zinc`, `cssVariables: true`. The default shadcn variables are overridden in `index.css`:

| Variable | Overridden? | Why |
|---|---|---|
| `--primary` / `--primary-foreground` | Yes | Brand teal |
| `--ring` | Yes | Match primary |
| `--accent` / `--accent-foreground` | Yes | Pale teal accent |
| `--success` / `--warning` / `--info` | Added | Not in shadcn default |
| `--chart-1..8` | Yes (5 from shadcn, 6–8 added) | Brand-aligned palette |
| `--shadow-*` | Added | shadcn relies on Tailwind defaults; we tune for both modes |
| `--motion-*` | Added | Not in shadcn |
| All other shadcn tokens | Left at defaults (zinc baseline) | Works with the brand |
| `Button` cursor | Yes | Default shadcn Button has no cursor override; we apply `cursor-pointer` so all interactive buttons get the hand cursor on hover |
| `Badge` variants | Extended | Added `success`, `warning`, `info` semantic variants (soft-tinted, matching the existing `destructive` recipe) |

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
| `secondary` | secondary | secondary-foreground | Neutral metadata |
| `outline` | transparent | foreground | Quiet metadata |
| `destructive` | destructive/10 | destructive | Errors, delete-confirmation tags |
| `success` | success/10 | success | Cleared, paid, positive states |
| `warning` | warning/10 | warning | Pending, attention-needed |
| `info` | info/10 | info | Informational tags |

```tsx
<Badge variant="success">Cleared</Badge>
<Badge variant="warning">Pending</Badge>
<Badge variant="info">Transaction</Badge>
```

**Chart palette colors are not statuses.** When you need a non-semantic tint to distinguish category-style values (e.g., the Movements page Type column uses `chart-6` violet for Transfer and `chart-7` orange for Liability Payment), opt out of variants and use className: `<Badge className="bg-chart-6/10 text-chart-6">Transfer</Badge>`. The explicit className signals "this is a deliberate non-semantic choice."

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

Recommended message style: short, sentence-case, ends in a period. Past tense for completed actions ("Saved."), contraction-friendly for failures ("Couldn't save.").

For destructive operations, prefer a confirmation dialog over a toast.

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
