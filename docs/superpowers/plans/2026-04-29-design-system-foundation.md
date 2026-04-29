# Design System Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish the Phase 3 design system foundation — brand color palette, typography (Inter for prose / IBM Plex Mono for tabular numerics), token structure, motion + shadow + radius scales, chart palette, and an internal `/design-system` showcase route — so every page built afterward arrives with the final visual language.

**Architecture:** Tailwind v4 with `@theme inline` consumes CSS custom properties defined on `:root` and `.dark`. All visual decisions live in `src/index.css` (tokens) and `docs/design-system.md` (rationale + reference). A new `<Numeric>` React component enforces the typography rule for currency, percentages, and dates in tabular contexts. A standalone `/design-system` HTML host page mounts a React-Router-driven showcase that renders every token and shadcn primitive in every state — it doubles as living documentation and a visual regression check.

**Tech Stack:** Tailwind CSS v4, shadcn/ui (`base-nova` style), React 19, Vite 8, React Router v7, `@fontsource-variable/inter`, `@fontsource-variable/ibm-plex-mono`, Vitest + React Testing Library.

**Scope boundary:** This plan ships *only* the design system foundation. Building the app shell, auth screens, onboarding, and feature pages are separate plans. After this plan: `dotnet build` and `pnpm dev` still produce a working app; the existing Razor + React-island UI is untouched; only the underlying tokens change appearance.

---

## File Structure

**Created:**
- `docs/design-system.md` — source of truth for all visual tokens, palette hex/OKLCH values, contrast ratios, typography rules, motion tokens, shadcn override list.
- `ProjectCeres.Client/src/components/Numeric.tsx` — opt-in monospace component for tabular numerics.
- `ProjectCeres.Client/src/components/Numeric.test.tsx` — unit tests for `<Numeric>`.
- `ProjectCeres.Client/src/design-system/main.tsx` — React entry for the showcase route.
- `ProjectCeres.Client/src/design-system/App.tsx` — router root.
- `ProjectCeres.Client/src/design-system/pages/Overview.tsx` — landing page of showcase.
- `ProjectCeres.Client/src/design-system/pages/Colors.tsx` — palette swatches with hex + contrast ratios.
- `ProjectCeres.Client/src/design-system/pages/Typography.tsx` — type scale + Inter/IBM Plex demo.
- `ProjectCeres.Client/src/design-system/pages/Spacing.tsx` — spacing + radius + shadow scales.
- `ProjectCeres.Client/src/design-system/pages/Motion.tsx` — duration + easing token demos.
- `ProjectCeres.Client/src/design-system/pages/Charts.tsx` — chart color palette swatches with sample recharts.
- `ProjectCeres.Client/src/design-system/pages/Components.tsx` — shadcn primitives in every state.
- `ProjectCeres.Client/src/design-system/components/ThemeToggle.tsx` — light/dark switch for the showcase.
- `ProjectCeres.Client/src/design-system/components/SwatchGrid.tsx` — reusable swatch row renderer.
- `ProjectCeres.Client/src/design-system/lib/contrast.ts` — small WCAG contrast helper.
- `ProjectCeres.Client/src/design-system/lib/contrast.test.ts` — tests for the helper.
- `ProjectCeres.Client/design-system.html` — HTML host for the showcase (Vite multi-page).
- `ProjectCeres.Client/src/components/ui/tabs.tsx` — added via `pnpm dlx shadcn add tabs`.
- `ProjectCeres.Client/src/components/ui/tooltip.tsx` — added via `pnpm dlx shadcn add tooltip`.

**Modified:**
- `ProjectCeres.Client/src/index.css` — replace Geist import with Inter + IBM Plex Mono; replace neutral-only OKLCH tokens with the brand palette; add motion / shadow tokens; add chart palette tokens; add `--font-mono` to `@theme inline`.
- `ProjectCeres.Client/package.json` — add `@fontsource-variable/inter`, `@fontsource-variable/ibm-plex-mono`, `react-router-dom`; remove `@fontsource-variable/geist` (no longer used).
- `ProjectCeres.Client/vite.config.ts` — declare both HTML entries (`index.html` and `design-system.html`) for the multi-page build.
- `ProjectCeres.Client/components.json` — change `baseColor` from `neutral` to `zinc` to match the new neutral scale.
- `docs/planning-phase3.md` — mark §6 Brand Foundation items as resolved with a pointer to `docs/design-system.md`.

---

## Brand Token Reference (use these exact values)

These are the source-of-truth values used in code steps below. Each step that writes a token uses these exact values.

**Light mode tokens (`:root`):**
```css
--background:           oklch(1.000 0.000 0);          /* #FFFFFF */
--foreground:           oklch(0.205 0.005 285);        /* near-black, slight cool */
--card:                 oklch(1.000 0.000 0);
--card-foreground:      oklch(0.205 0.005 285);
--popover:              oklch(1.000 0.000 0);
--popover-foreground:   oklch(0.205 0.005 285);
--primary:              oklch(0.520 0.110 195);        /* Deep teal #0D7C7C */
--primary-foreground:   oklch(0.985 0.000 0);
--secondary:            oklch(0.965 0.005 285);        /* Zinc-100 */
--secondary-foreground: oklch(0.205 0.005 285);
--muted:                oklch(0.965 0.005 285);
--muted-foreground:     oklch(0.500 0.010 285);        /* Zinc-500 */
--accent:               oklch(0.945 0.020 195);        /* Pale teal */
--accent-foreground:    oklch(0.300 0.080 195);
--destructive:          oklch(0.580 0.220 18);         /* Rose-600 */
--success:              oklch(0.620 0.180 150);        /* Emerald-600 */
--warning:              oklch(0.770 0.170 80);         /* Amber-500 */
--info:                 oklch(0.670 0.150 235);        /* Sky-500 */
--border:               oklch(0.910 0.005 285);        /* Zinc-200 */
--input:                oklch(0.910 0.005 285);
--ring:                 oklch(0.520 0.110 195);        /* Match primary */
--chart-1:              oklch(0.520 0.110 195);        /* Teal */
--chart-2:              oklch(0.620 0.180 150);        /* Emerald */
--chart-3:              oklch(0.670 0.150 235);        /* Sky */
--chart-4:              oklch(0.770 0.170 80);         /* Amber */
--chart-5:              oklch(0.580 0.220 18);         /* Rose */
--chart-6:              oklch(0.580 0.180 305);        /* Violet */
--chart-7:              oklch(0.700 0.160 60);         /* Orange */
--chart-8:              oklch(0.500 0.080 240);        /* Slate-blue */
--radius:               0.625rem;                      /* 10px base */
--shadow-sm:            0 1px 2px 0 rgb(0 0 0 / 0.04);
--shadow:               0 1px 3px 0 rgb(0 0 0 / 0.06), 0 1px 2px -1px rgb(0 0 0 / 0.06);
--shadow-md:            0 4px 6px -1px rgb(0 0 0 / 0.08), 0 2px 4px -2px rgb(0 0 0 / 0.08);
--shadow-lg:            0 10px 15px -3px rgb(0 0 0 / 0.10), 0 4px 6px -4px rgb(0 0 0 / 0.08);
--motion-duration-fast:    120ms;
--motion-duration-base:    180ms;
--motion-duration-slow:    260ms;
--motion-easing-standard:  cubic-bezier(0.2, 0, 0, 1);
--motion-easing-emphasized:cubic-bezier(0.3, 0, 0, 1);
--sidebar:                 oklch(0.985 0.005 285);
--sidebar-foreground:      oklch(0.205 0.005 285);
--sidebar-primary:         oklch(0.520 0.110 195);
--sidebar-primary-foreground: oklch(0.985 0.000 0);
--sidebar-accent:          oklch(0.945 0.020 195);
--sidebar-accent-foreground:oklch(0.300 0.080 195);
--sidebar-border:          oklch(0.910 0.005 285);
--sidebar-ring:            oklch(0.520 0.110 195);
```

**Dark mode tokens (`.dark`):**
```css
--background:           oklch(0.155 0.005 285);        /* Near-black with cool tint */
--foreground:           oklch(0.965 0.005 285);
--card:                 oklch(0.205 0.005 285);
--card-foreground:      oklch(0.965 0.005 285);
--popover:              oklch(0.205 0.005 285);
--popover-foreground:   oklch(0.965 0.005 285);
--primary:              oklch(0.770 0.130 180);        /* Brighter teal #5EEAD4 */
--primary-foreground:   oklch(0.155 0.005 285);
--secondary:            oklch(0.270 0.005 285);
--secondary-foreground: oklch(0.965 0.005 285);
--muted:                oklch(0.270 0.005 285);
--muted-foreground:     oklch(0.700 0.010 285);
--accent:               oklch(0.310 0.040 195);
--accent-foreground:    oklch(0.900 0.040 195);
--destructive:          oklch(0.700 0.190 22);
--success:              oklch(0.720 0.170 150);
--warning:              oklch(0.820 0.160 80);
--info:                 oklch(0.750 0.140 235);
--border:               oklch(1 0 0 / 10%);
--input:                oklch(1 0 0 / 15%);
--ring:                 oklch(0.770 0.130 180);
--chart-1:              oklch(0.770 0.130 180);
--chart-2:              oklch(0.720 0.170 150);
--chart-3:              oklch(0.750 0.140 235);
--chart-4:              oklch(0.820 0.160 80);
--chart-5:              oklch(0.700 0.190 22);
--chart-6:              oklch(0.700 0.170 305);
--chart-7:              oklch(0.770 0.160 60);
--chart-8:              oklch(0.620 0.080 240);
--shadow-sm:            0 1px 2px 0 rgb(0 0 0 / 0.40);
--shadow:               0 1px 3px 0 rgb(0 0 0 / 0.50), 0 1px 2px -1px rgb(0 0 0 / 0.40);
--shadow-md:            0 4px 6px -1px rgb(0 0 0 / 0.50), 0 2px 4px -2px rgb(0 0 0 / 0.40);
--shadow-lg:            0 10px 15px -3px rgb(0 0 0 / 0.60), 0 4px 6px -4px rgb(0 0 0 / 0.40);
--sidebar:                 oklch(0.205 0.005 285);
--sidebar-foreground:      oklch(0.965 0.005 285);
--sidebar-primary:         oklch(0.770 0.130 180);
--sidebar-primary-foreground: oklch(0.155 0.005 285);
--sidebar-accent:          oklch(0.310 0.040 195);
--sidebar-accent-foreground:oklch(0.900 0.040 195);
--sidebar-border:          oklch(1 0 0 / 10%);
--sidebar-ring:            oklch(0.770 0.130 180);
```

---

## Task 1: Add font and router dependencies; remove Geist

**Files:**
- Modify: `ProjectCeres.Client/package.json`

- [ ] **Step 1: Add Inter and IBM Plex Mono, add react-router-dom, remove Geist**

Run from `ProjectCeres.Client/`:
```bash
pnpm remove @fontsource-variable/geist
pnpm add @fontsource-variable/inter @fontsource-variable/ibm-plex-mono react-router-dom
```

Expected: `package.json` shows `@fontsource-variable/inter`, `@fontsource-variable/ibm-plex-mono`, `react-router-dom` under `dependencies`; `@fontsource-variable/geist` removed.

- [ ] **Step 2: Verify build still passes**

Run: `pnpm --dir ProjectCeres.Client build`
Expected: build succeeds (the existing `index.css` still imports Geist — that import will fail. Continue to Task 2 immediately; do not commit yet.)

If build fails because of the Geist import, that is expected — proceed to Task 2 before committing.

---

## Task 2: Replace tokens in `index.css` with brand palette

**Files:**
- Modify: `ProjectCeres.Client/src/index.css`

- [ ] **Step 1: Replace the entire `index.css` file**

Replace the full contents with:

```css
@import "tailwindcss";
@import "tw-animate-css";
@import "shadcn/tailwind.css";
@import "@fontsource-variable/inter";
@import "@fontsource-variable/ibm-plex-mono";

@custom-variant dark (&:is(.dark *));

@theme inline {
    --font-heading: var(--font-sans);
    --font-sans: 'Inter Variable', system-ui, -apple-system, sans-serif;
    --font-mono: 'IBM Plex Mono', ui-monospace, SFMono-Regular, Menlo, monospace;

    --color-sidebar-ring: var(--sidebar-ring);
    --color-sidebar-border: var(--sidebar-border);
    --color-sidebar-accent-foreground: var(--sidebar-accent-foreground);
    --color-sidebar-accent: var(--sidebar-accent);
    --color-sidebar-primary-foreground: var(--sidebar-primary-foreground);
    --color-sidebar-primary: var(--sidebar-primary);
    --color-sidebar-foreground: var(--sidebar-foreground);
    --color-sidebar: var(--sidebar);

    --color-chart-1: var(--chart-1);
    --color-chart-2: var(--chart-2);
    --color-chart-3: var(--chart-3);
    --color-chart-4: var(--chart-4);
    --color-chart-5: var(--chart-5);
    --color-chart-6: var(--chart-6);
    --color-chart-7: var(--chart-7);
    --color-chart-8: var(--chart-8);

    --color-ring: var(--ring);
    --color-input: var(--input);
    --color-border: var(--border);
    --color-destructive: var(--destructive);
    --color-success: var(--success);
    --color-warning: var(--warning);
    --color-info: var(--info);
    --color-accent-foreground: var(--accent-foreground);
    --color-accent: var(--accent);
    --color-muted-foreground: var(--muted-foreground);
    --color-muted: var(--muted);
    --color-secondary-foreground: var(--secondary-foreground);
    --color-secondary: var(--secondary);
    --color-primary-foreground: var(--primary-foreground);
    --color-primary: var(--primary);
    --color-popover-foreground: var(--popover-foreground);
    --color-popover: var(--popover);
    --color-card-foreground: var(--card-foreground);
    --color-card: var(--card);
    --color-foreground: var(--foreground);
    --color-background: var(--background);

    --radius-sm: calc(var(--radius) * 0.6);
    --radius-md: calc(var(--radius) * 0.8);
    --radius-lg: var(--radius);
    --radius-xl: calc(var(--radius) * 1.4);
    --radius-2xl: calc(var(--radius) * 1.8);
    --radius-3xl: calc(var(--radius) * 2.2);
    --radius-4xl: calc(var(--radius) * 2.6);
}

/* Switch thumb translate — driven by base-ui data attributes, not Tailwind variants */
[data-slot="switch"][data-checked] .switch-thumb { translate: calc(100% - 2px) 0; }
[data-slot="switch"][data-unchecked] .switch-thumb { translate: 0 0; }
[data-slot="switch"][data-checked] { background-color: var(--primary); }
[data-slot="switch"][data-unchecked] { background-color: var(--input); }

:root {
    --background: oklch(1.000 0.000 0);
    --foreground: oklch(0.205 0.005 285);
    --card: oklch(1.000 0.000 0);
    --card-foreground: oklch(0.205 0.005 285);
    --popover: oklch(1.000 0.000 0);
    --popover-foreground: oklch(0.205 0.005 285);
    --primary: oklch(0.520 0.110 195);
    --primary-foreground: oklch(0.985 0.000 0);
    --secondary: oklch(0.965 0.005 285);
    --secondary-foreground: oklch(0.205 0.005 285);
    --muted: oklch(0.965 0.005 285);
    --muted-foreground: oklch(0.500 0.010 285);
    --accent: oklch(0.945 0.020 195);
    --accent-foreground: oklch(0.300 0.080 195);
    --destructive: oklch(0.580 0.220 18);
    --success: oklch(0.620 0.180 150);
    --warning: oklch(0.770 0.170 80);
    --info: oklch(0.670 0.150 235);
    --border: oklch(0.910 0.005 285);
    --input: oklch(0.910 0.005 285);
    --ring: oklch(0.520 0.110 195);
    --chart-1: oklch(0.520 0.110 195);
    --chart-2: oklch(0.620 0.180 150);
    --chart-3: oklch(0.670 0.150 235);
    --chart-4: oklch(0.770 0.170 80);
    --chart-5: oklch(0.580 0.220 18);
    --chart-6: oklch(0.580 0.180 305);
    --chart-7: oklch(0.700 0.160 60);
    --chart-8: oklch(0.500 0.080 240);
    --radius: 0.625rem;
    --shadow-sm: 0 1px 2px 0 rgb(0 0 0 / 0.04);
    --shadow:    0 1px 3px 0 rgb(0 0 0 / 0.06), 0 1px 2px -1px rgb(0 0 0 / 0.06);
    --shadow-md: 0 4px 6px -1px rgb(0 0 0 / 0.08), 0 2px 4px -2px rgb(0 0 0 / 0.08);
    --shadow-lg: 0 10px 15px -3px rgb(0 0 0 / 0.10), 0 4px 6px -4px rgb(0 0 0 / 0.08);
    --motion-duration-fast: 120ms;
    --motion-duration-base: 180ms;
    --motion-duration-slow: 260ms;
    --motion-easing-standard:   cubic-bezier(0.2, 0, 0, 1);
    --motion-easing-emphasized: cubic-bezier(0.3, 0, 0, 1);
    --sidebar: oklch(0.985 0.005 285);
    --sidebar-foreground: oklch(0.205 0.005 285);
    --sidebar-primary: oklch(0.520 0.110 195);
    --sidebar-primary-foreground: oklch(0.985 0.000 0);
    --sidebar-accent: oklch(0.945 0.020 195);
    --sidebar-accent-foreground: oklch(0.300 0.080 195);
    --sidebar-border: oklch(0.910 0.005 285);
    --sidebar-ring: oklch(0.520 0.110 195);
}

.dark {
    --background: oklch(0.155 0.005 285);
    --foreground: oklch(0.965 0.005 285);
    --card: oklch(0.205 0.005 285);
    --card-foreground: oklch(0.965 0.005 285);
    --popover: oklch(0.205 0.005 285);
    --popover-foreground: oklch(0.965 0.005 285);
    --primary: oklch(0.770 0.130 180);
    --primary-foreground: oklch(0.155 0.005 285);
    --secondary: oklch(0.270 0.005 285);
    --secondary-foreground: oklch(0.965 0.005 285);
    --muted: oklch(0.270 0.005 285);
    --muted-foreground: oklch(0.700 0.010 285);
    --accent: oklch(0.310 0.040 195);
    --accent-foreground: oklch(0.900 0.040 195);
    --destructive: oklch(0.700 0.190 22);
    --success: oklch(0.720 0.170 150);
    --warning: oklch(0.820 0.160 80);
    --info: oklch(0.750 0.140 235);
    --border: oklch(1 0 0 / 10%);
    --input: oklch(1 0 0 / 15%);
    --ring: oklch(0.770 0.130 180);
    --chart-1: oklch(0.770 0.130 180);
    --chart-2: oklch(0.720 0.170 150);
    --chart-3: oklch(0.750 0.140 235);
    --chart-4: oklch(0.820 0.160 80);
    --chart-5: oklch(0.700 0.190 22);
    --chart-6: oklch(0.700 0.170 305);
    --chart-7: oklch(0.770 0.160 60);
    --chart-8: oklch(0.620 0.080 240);
    --shadow-sm: 0 1px 2px 0 rgb(0 0 0 / 0.40);
    --shadow:    0 1px 3px 0 rgb(0 0 0 / 0.50), 0 1px 2px -1px rgb(0 0 0 / 0.40);
    --shadow-md: 0 4px 6px -1px rgb(0 0 0 / 0.50), 0 2px 4px -2px rgb(0 0 0 / 0.40);
    --shadow-lg: 0 10px 15px -3px rgb(0 0 0 / 0.60), 0 4px 6px -4px rgb(0 0 0 / 0.40);
    --sidebar: oklch(0.205 0.005 285);
    --sidebar-foreground: oklch(0.965 0.005 285);
    --sidebar-primary: oklch(0.770 0.130 180);
    --sidebar-primary-foreground: oklch(0.155 0.005 285);
    --sidebar-accent: oklch(0.310 0.040 195);
    --sidebar-accent-foreground: oklch(0.900 0.040 195);
    --sidebar-border: oklch(1 0 0 / 10%);
    --sidebar-ring: oklch(0.770 0.130 180);
}

@layer base {
  * { @apply border-border outline-ring/50; }
  body { @apply bg-background text-foreground; }
  html { @apply font-sans; }
}
```

- [ ] **Step 2: Update `components.json` baseColor**

Replace the `"baseColor": "neutral"` line with `"baseColor": "zinc"`.

- [ ] **Step 3: Run dev server and verify visually**

Run: `pnpm --dir ProjectCeres.Client dev`
Expected: dev server starts; opening the existing app shows Inter font has replaced Geist; the existing React island components (navbar, badges, charts) still render; primary-colored elements (toggles, primary buttons) now appear teal rather than gray.

Stop the dev server (Ctrl+C).

- [ ] **Step 4: Run build**

Run: `pnpm --dir ProjectCeres.Client build`
Expected: PASS — TypeScript and Vite build succeed.

- [ ] **Step 5: Run existing tests**

Run: `pnpm --dir ProjectCeres.Client test`
Expected: All existing tests still pass — none reference token names that changed.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/package.json ProjectCeres.Client/pnpm-lock.yaml ProjectCeres.Client/src/index.css ProjectCeres.Client/components.json
git commit -m "feat(design-system): adopt brand palette and Inter+IBM Plex Mono typography"
```

---

## Task 3: Add `Numeric` component (typography rule enforcement)

**Files:**
- Create: `ProjectCeres.Client/src/components/Numeric.tsx`
- Test: `ProjectCeres.Client/src/components/Numeric.test.tsx`

The `<Numeric>` component renders any numeric data (currency, percentages, dates) in IBM Plex Mono with `tabular-nums` enabled. Use it in *tabular contexts only* (table cells, KPI cards, chart axis labels). Sentences with embedded percentages should keep using Inter — do not wrap them in `<Numeric>`.

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Client/src/components/Numeric.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Numeric } from './Numeric';

describe('Numeric', () => {
  it('renders the children', () => {
    render(<Numeric>1.234,56</Numeric>);
    expect(screen.getByText('1.234,56')).toBeDefined();
  });

  it('applies the mono font and tabular-nums class', () => {
    render(<Numeric>42%</Numeric>);
    const el = screen.getByText('42%');
    expect(el.className).toContain('font-mono');
    expect(el.className).toContain('tabular-nums');
  });

  it('preserves caller-supplied className', () => {
    render(<Numeric className="text-destructive">-€500</Numeric>);
    const el = screen.getByText('-€500');
    expect(el.className).toContain('text-destructive');
    expect(el.className).toContain('font-mono');
  });

  it('renders a span by default and supports `as`', () => {
    const { rerender } = render(<Numeric>1</Numeric>);
    expect(screen.getByText('1').tagName).toBe('SPAN');
    rerender(<Numeric as="td">2</Numeric>);
    expect(screen.getByText('2').tagName).toBe('TD');
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `pnpm --dir ProjectCeres.Client test src/components/Numeric.test.tsx`
Expected: FAIL — `Numeric` module not found.

- [ ] **Step 3: Implement `Numeric`**

Create `ProjectCeres.Client/src/components/Numeric.tsx`:

```tsx
import { type ElementType, type HTMLAttributes } from 'react';
import { cn } from '@/lib/utils';

type NumericProps = HTMLAttributes<HTMLElement> & {
  /** HTML element to render. Defaults to `span`. Use `td` inside tables. */
  as?: ElementType;
};

/**
 * Tabular numeric text. Use for currency, percentages, and dates in
 * data contexts (KPI cards, table cells, chart axes). Do NOT use for
 * percentages embedded in prose — keep those in the surrounding font.
 */
export function Numeric({ as, className, children, ...rest }: NumericProps) {
  const Tag = (as ?? 'span') as ElementType;
  return (
    <Tag className={cn('font-mono tabular-nums', className)} {...rest}>
      {children}
    </Tag>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `pnpm --dir ProjectCeres.Client test src/components/Numeric.test.tsx`
Expected: PASS — all four tests pass.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/components/Numeric.tsx ProjectCeres.Client/src/components/Numeric.test.tsx
git commit -m "feat(design-system): add Numeric component for tabular numerics"
```

---

## Task 4: Add `Tabs` and `Tooltip` shadcn primitives

**Files:**
- Create: `ProjectCeres.Client/src/components/ui/tabs.tsx` (generated)
- Create: `ProjectCeres.Client/src/components/ui/tooltip.tsx` (generated)

- [ ] **Step 1: Add shadcn primitives**

From repo root:
```bash
pnpm --dir ProjectCeres.Client dlx shadcn add tabs tooltip
```

Expected: two new files appear under `src/components/ui/`. Accept any auto-installed Radix dependencies.

- [ ] **Step 2: Verify build**

Run: `pnpm --dir ProjectCeres.Client build`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/components/ui/tabs.tsx ProjectCeres.Client/src/components/ui/tooltip.tsx ProjectCeres.Client/package.json ProjectCeres.Client/pnpm-lock.yaml
git commit -m "feat(design-system): add tabs and tooltip shadcn primitives"
```

---

## Task 5: WCAG contrast helper

**Files:**
- Create: `ProjectCeres.Client/src/design-system/lib/contrast.ts`
- Test: `ProjectCeres.Client/src/design-system/lib/contrast.test.ts`

A small helper that computes the WCAG 2.1 contrast ratio between two CSS colors resolved against the live document. The showcase Colors page uses it to display contrast ratios next to each swatch.

- [ ] **Step 1: Write the failing test**

Create `ProjectCeres.Client/src/design-system/lib/contrast.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { contrastRatio, formatRatio } from './contrast';

describe('contrastRatio', () => {
  it('returns 21 for black on white', () => {
    expect(contrastRatio('rgb(0,0,0)', 'rgb(255,255,255)')).toBeCloseTo(21, 0);
  });

  it('returns 1 for identical colors', () => {
    expect(contrastRatio('rgb(120,120,120)', 'rgb(120,120,120)')).toBeCloseTo(1, 1);
  });

  it('is symmetric', () => {
    const a = contrastRatio('rgb(20,40,60)', 'rgb(200,210,220)');
    const b = contrastRatio('rgb(200,210,220)', 'rgb(20,40,60)');
    expect(a).toBeCloseTo(b, 4);
  });
});

describe('formatRatio', () => {
  it('formats with two decimals and AA/AAA labels', () => {
    expect(formatRatio(21)).toBe('21.00 : 1 (AAA)');
    expect(formatRatio(4.6)).toBe('4.60 : 1 (AA)');
    expect(formatRatio(3.2)).toBe('3.20 : 1 (Large only)');
    expect(formatRatio(2.0)).toBe('2.00 : 1 (Fail)');
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `pnpm --dir ProjectCeres.Client test src/design-system/lib/contrast.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement the helper**

Create `ProjectCeres.Client/src/design-system/lib/contrast.ts`:

```ts
function parseRgb(input: string): [number, number, number] {
  const m = input.match(/rgba?\(([^)]+)\)/i);
  if (!m) throw new Error(`Cannot parse color: ${input}`);
  const parts = m[1].split(',').map((s) => parseFloat(s.trim()));
  return [parts[0], parts[1], parts[2]];
}

function relativeLuminance([r, g, b]: [number, number, number]): number {
  const channel = (c: number) => {
    const v = c / 255;
    return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
  };
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

/**
 * Compute the WCAG 2.1 contrast ratio between two `rgb(r,g,b)` strings.
 * Use `getComputedStyle(el).backgroundColor` / `.color` to resolve
 * CSS custom properties to concrete rgb() values before passing in.
 */
export function contrastRatio(a: string, b: string): number {
  const la = relativeLuminance(parseRgb(a));
  const lb = relativeLuminance(parseRgb(b));
  const [light, dark] = la > lb ? [la, lb] : [lb, la];
  return (light + 0.05) / (dark + 0.05);
}

export function formatRatio(ratio: number): string {
  const r = ratio.toFixed(2);
  if (ratio >= 7) return `${r} : 1 (AAA)`;
  if (ratio >= 4.5) return `${r} : 1 (AA)`;
  if (ratio >= 3) return `${r} : 1 (Large only)`;
  return `${r} : 1 (Fail)`;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `pnpm --dir ProjectCeres.Client test src/design-system/lib/contrast.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/design-system/lib/contrast.ts ProjectCeres.Client/src/design-system/lib/contrast.test.ts
git commit -m "feat(design-system): add WCAG contrast helper for showcase page"
```

---

## Task 6: Configure Vite multi-page build for `/design-system.html`

**Files:**
- Create: `ProjectCeres.Client/design-system.html`
- Modify: `ProjectCeres.Client/vite.config.ts`

The showcase route is hosted on a second HTML entry rather than added to `main.tsx`. This keeps the Razor-island bootstrap untouched and gives the showcase a clean URL.

- [ ] **Step 1: Read current `vite.config.ts`**

Run: `cat ProjectCeres.Client/vite.config.ts`

Note its current contents — the modification below adds a `rollupOptions.input` block. Preserve any existing plugins/alias config; only add the `build` section if it is missing.

- [ ] **Step 2: Update `vite.config.ts` with multi-page input**

Modify `ProjectCeres.Client/vite.config.ts` so the exported `defineConfig` includes:

```ts
import { defineConfig } from 'vite';
import { resolve } from 'node:path';
// ... preserve existing plugin imports (react, tailwindcss, tsconfigPaths, etc.)

export default defineConfig({
  // ... preserve existing plugins, resolve.alias, etc.
  build: {
    rollupOptions: {
      input: {
        main: resolve(__dirname, 'index.html'),
        designSystem: resolve(__dirname, 'design-system.html'),
      },
    },
  },
});
```

If the file uses `import.meta.dirname` (Node 20+) instead of `__dirname`, use that — match the existing convention.

- [ ] **Step 3: Create the host HTML page**

Create `ProjectCeres.Client/design-system.html`:

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Project Ceres — Design System</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/design-system/main.tsx"></script>
  </body>
</html>
```

- [ ] **Step 4: Verify build (will fail because main.tsx does not exist yet)**

Run: `pnpm --dir ProjectCeres.Client build`
Expected: FAIL with "Could not resolve /src/design-system/main.tsx" — that is fine; Task 7 creates it. Do not commit yet.

---

## Task 7: Showcase router root

**Files:**
- Create: `ProjectCeres.Client/src/design-system/main.tsx`
- Create: `ProjectCeres.Client/src/design-system/App.tsx`
- Create: `ProjectCeres.Client/src/design-system/components/ThemeToggle.tsx`

- [ ] **Step 1: Create the React entry**

Create `ProjectCeres.Client/src/design-system/main.tsx`:

```tsx
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import '../index.css';
import { App } from './App';

const root = document.getElementById('root');
if (!root) throw new Error('design-system root element missing');

createRoot(root).render(
  <StrictMode>
    <BrowserRouter basename="/design-system.html">
      <App />
    </BrowserRouter>
  </StrictMode>,
);
```

- [ ] **Step 2: Create the theme toggle**

Create `ProjectCeres.Client/src/design-system/components/ThemeToggle.tsx`:

```tsx
import { Moon, Sun } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';

export function ThemeToggle() {
  const [dark, setDark] = useState(() =>
    document.documentElement.classList.contains('dark'),
  );

  useEffect(() => {
    document.documentElement.classList.toggle('dark', dark);
  }, [dark]);

  return (
    <Button
      variant="outline"
      size="sm"
      onClick={() => setDark((d) => !d)}
      aria-label={dark ? 'Switch to light mode' : 'Switch to dark mode'}
    >
      {dark ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
      <span className="ml-2">{dark ? 'Light' : 'Dark'}</span>
    </Button>
  );
}
```

- [ ] **Step 3: Create the App with sidebar nav**

Create `ProjectCeres.Client/src/design-system/App.tsx`:

```tsx
import { NavLink, Route, Routes } from 'react-router-dom';
import { ThemeToggle } from './components/ThemeToggle';
import { Overview } from './pages/Overview';
import { Colors } from './pages/Colors';
import { Typography } from './pages/Typography';
import { Spacing } from './pages/Spacing';
import { Motion } from './pages/Motion';
import { Charts } from './pages/Charts';
import { Components } from './pages/Components';

const sections = [
  { to: '/', label: 'Overview', end: true },
  { to: '/colors', label: 'Colors' },
  { to: '/typography', label: 'Typography' },
  { to: '/spacing', label: 'Spacing, Radius & Shadow' },
  { to: '/motion', label: 'Motion' },
  { to: '/charts', label: 'Charts' },
  { to: '/components', label: 'Components' },
];

export function App() {
  return (
    <div className="grid min-h-screen grid-cols-[240px_1fr] bg-background text-foreground">
      <aside className="border-r border-border p-6">
        <header className="mb-6 flex items-center justify-between">
          <h1 className="text-lg font-semibold">Ceres DS</h1>
          <ThemeToggle />
        </header>
        <nav className="flex flex-col gap-1">
          {sections.map((s) => (
            <NavLink
              key={s.to}
              to={s.to}
              end={s.end}
              className={({ isActive }) =>
                [
                  'rounded-md px-3 py-2 text-sm transition-colors',
                  isActive
                    ? 'bg-accent text-accent-foreground'
                    : 'text-muted-foreground hover:bg-muted hover:text-foreground',
                ].join(' ')
              }
            >
              {s.label}
            </NavLink>
          ))}
        </nav>
      </aside>
      <main className="overflow-y-auto p-10">
        <Routes>
          <Route path="/" element={<Overview />} />
          <Route path="/colors" element={<Colors />} />
          <Route path="/typography" element={<Typography />} />
          <Route path="/spacing" element={<Spacing />} />
          <Route path="/motion" element={<Motion />} />
          <Route path="/charts" element={<Charts />} />
          <Route path="/components" element={<Components />} />
        </Routes>
      </main>
    </div>
  );
}
```

- [ ] **Step 4: Build will still fail because page modules do not exist — proceed to Task 8**

No commit yet.

---

## Task 8: Showcase pages — Overview, Colors, Typography

**Files:**
- Create: `ProjectCeres.Client/src/design-system/components/SwatchGrid.tsx`
- Create: `ProjectCeres.Client/src/design-system/pages/Overview.tsx`
- Create: `ProjectCeres.Client/src/design-system/pages/Colors.tsx`
- Create: `ProjectCeres.Client/src/design-system/pages/Typography.tsx`

- [ ] **Step 1: Create the SwatchGrid helper**

Create `ProjectCeres.Client/src/design-system/components/SwatchGrid.tsx`:

```tsx
import { useEffect, useRef, useState } from 'react';
import { contrastRatio, formatRatio } from '../lib/contrast';

export type Swatch = {
  /** CSS variable name without leading `--` */
  token: string;
  label: string;
  /** Token whose color this swatch is meant to be readable against */
  contrastAgainst?: string;
};

export function SwatchGrid({ swatches }: { swatches: Swatch[] }) {
  return (
    <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-4">
      {swatches.map((s) => (
        <SwatchCard key={s.token} swatch={s} />
      ))}
    </div>
  );
}

function SwatchCard({ swatch }: { swatch: Swatch }) {
  const ref = useRef<HTMLDivElement>(null);
  const [info, setInfo] = useState<{ rgb: string; ratio?: string }>({
    rgb: '—',
  });

  useEffect(() => {
    if (!ref.current) return;
    const styles = getComputedStyle(ref.current);
    const bg = styles.backgroundColor;
    let ratio: string | undefined;
    if (swatch.contrastAgainst) {
      const probe = document.createElement('div');
      probe.style.color = `var(--${swatch.contrastAgainst})`;
      ref.current.appendChild(probe);
      const fg = getComputedStyle(probe).color;
      ref.current.removeChild(probe);
      ratio = formatRatio(contrastRatio(bg, fg));
    }
    setInfo({ rgb: bg, ratio });
  }, [swatch.token, swatch.contrastAgainst]);

  return (
    <div className="overflow-hidden rounded-md border border-border">
      <div
        ref={ref}
        className="h-20"
        style={{ backgroundColor: `var(--${swatch.token})` }}
      />
      <div className="space-y-1 p-3 text-sm">
        <div className="font-medium">{swatch.label}</div>
        <div className="font-mono text-xs text-muted-foreground">
          --{swatch.token}
        </div>
        <div className="font-mono text-xs text-muted-foreground">{info.rgb}</div>
        {info.ratio && (
          <div className="font-mono text-xs text-muted-foreground">
            vs --{swatch.contrastAgainst}: {info.ratio}
          </div>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Create the Overview page**

Create `ProjectCeres.Client/src/design-system/pages/Overview.tsx`:

```tsx
export function Overview() {
  return (
    <div className="prose max-w-2xl">
      <h1 className="text-3xl font-semibold">Project Ceres Design System</h1>
      <p className="mt-4 text-muted-foreground">
        This is the living reference for every visual decision in the Ceres
        product. Tokens defined here drive every page; if a value is not
        listed here, it should not be hard-coded in components.
      </p>
      <p className="mt-4 text-muted-foreground">
        Use the toggle at the top of the sidebar to flip between light and
        dark modes — every swatch and component should respond.
      </p>
      <p className="mt-4 text-muted-foreground">
        Source of truth document:{' '}
        <code className="rounded bg-muted px-1 py-0.5 text-sm">
          docs/design-system.md
        </code>
      </p>
    </div>
  );
}
```

- [ ] **Step 3: Create the Colors page**

Create `ProjectCeres.Client/src/design-system/pages/Colors.tsx`:

```tsx
import { SwatchGrid, type Swatch } from '../components/SwatchGrid';

const surface: Swatch[] = [
  { token: 'background', label: 'Background', contrastAgainst: 'foreground' },
  { token: 'card', label: 'Card', contrastAgainst: 'card-foreground' },
  { token: 'popover', label: 'Popover', contrastAgainst: 'popover-foreground' },
  { token: 'muted', label: 'Muted', contrastAgainst: 'muted-foreground' },
  { token: 'accent', label: 'Accent', contrastAgainst: 'accent-foreground' },
  { token: 'border', label: 'Border' },
];

const brand: Swatch[] = [
  { token: 'primary', label: 'Primary', contrastAgainst: 'primary-foreground' },
  { token: 'secondary', label: 'Secondary', contrastAgainst: 'secondary-foreground' },
];

const semantic: Swatch[] = [
  { token: 'success', label: 'Success (income)', contrastAgainst: 'background' },
  { token: 'destructive', label: 'Destructive (expense)', contrastAgainst: 'background' },
  { token: 'warning', label: 'Warning', contrastAgainst: 'background' },
  { token: 'info', label: 'Info', contrastAgainst: 'background' },
];

export function Colors() {
  return (
    <div className="space-y-10">
      <header>
        <h1 className="text-2xl font-semibold">Colors</h1>
        <p className="mt-2 text-muted-foreground">
          All colors are CSS custom properties on <code>:root</code> and{' '}
          <code>.dark</code>. Components read them via Tailwind utilities like{' '}
          <code>bg-primary</code> or <code>text-muted-foreground</code>. Never
          hard-code hex values in components.
        </p>
      </header>

      <section>
        <h2 className="mb-4 text-xl font-medium">Surface</h2>
        <SwatchGrid swatches={surface} />
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Brand</h2>
        <SwatchGrid swatches={brand} />
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Semantic</h2>
        <SwatchGrid swatches={semantic} />
      </section>
    </div>
  );
}
```

- [ ] **Step 4: Create the Typography page**

Create `ProjectCeres.Client/src/design-system/pages/Typography.tsx`:

```tsx
import { Numeric } from '@/components/Numeric';

const typeScale = [
  { className: 'text-xs',  label: 'xs  · 12px / Caption' },
  { className: 'text-sm',  label: 'sm  · 14px / Body small' },
  { className: 'text-base',label: 'base · 16px / Body' },
  { className: 'text-lg',  label: 'lg  · 18px / Lead' },
  { className: 'text-xl',  label: 'xl  · 20px / Subhead' },
  { className: 'text-2xl', label: '2xl · 24px / H3' },
  { className: 'text-3xl', label: '3xl · 30px / H2' },
  { className: 'text-4xl', label: '4xl · 36px / H1' },
];

export function Typography() {
  return (
    <div className="space-y-10">
      <header>
        <h1 className="text-2xl font-semibold">Typography</h1>
        <p className="mt-2 text-muted-foreground">
          <strong>Inter</strong> for all UI text and prose.{' '}
          <strong>IBM Plex Mono</strong> via the <code>&lt;Numeric&gt;</code>{' '}
          component for currency, percentages, and dates in tabular contexts.
          Percentages embedded in sentences stay in Inter.
        </p>
      </header>

      <section>
        <h2 className="mb-4 text-xl font-medium">Sans (Inter) — type scale</h2>
        <div className="space-y-3">
          {typeScale.map((t) => (
            <div key={t.className} className={t.className}>
              {t.label} — The quick brown fox jumps over the lazy dog.
            </div>
          ))}
        </div>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Mono (IBM Plex Mono) — Numeric</h2>
        <table className="w-full max-w-md border-collapse text-sm">
          <thead>
            <tr className="border-b border-border text-left">
              <th className="py-2">Account</th>
              <th className="py-2 text-right">Balance</th>
              <th className="py-2 text-right">% of total</th>
            </tr>
          </thead>
          <tbody>
            {[
              ['Checking',   '€1.234,56',  '42%'],
              ['Savings',    '€8.500,00',  '38%'],
              ['Credit card','-€512,30',   '20%'],
            ].map(([account, balance, pct]) => (
              <tr key={account} className="border-b border-border">
                <td className="py-2">{account}</td>
                <td className="py-2 text-right"><Numeric>{balance}</Numeric></td>
                <td className="py-2 text-right"><Numeric>{pct}</Numeric></td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Mixed in prose (stays Inter)</h2>
        <p className="max-w-prose text-base">
          You've spent 73% of your Groceries budget so far this month —
          on track to finish around 95% by the period end. The percentages
          here are part of the sentence and stay in Inter for readability.
        </p>
      </section>
    </div>
  );
}
```

- [ ] **Step 5: Verify build still fails (Spacing/Motion/Charts/Components missing)**

Run: `pnpm --dir ProjectCeres.Client build`
Expected: FAIL — Spacing, Motion, Charts, Components not yet created. Continue to Task 9. No commit yet.

---

## Task 9: Showcase pages — Spacing, Motion, Charts, Components

**Files:**
- Create: `ProjectCeres.Client/src/design-system/pages/Spacing.tsx`
- Create: `ProjectCeres.Client/src/design-system/pages/Motion.tsx`
- Create: `ProjectCeres.Client/src/design-system/pages/Charts.tsx`
- Create: `ProjectCeres.Client/src/design-system/pages/Components.tsx`

- [ ] **Step 1: Create the Spacing page**

Create `ProjectCeres.Client/src/design-system/pages/Spacing.tsx`:

```tsx
const spacingSteps = [0, 1, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 24];
const radii = ['rounded-sm', 'rounded-md', 'rounded-lg', 'rounded-xl', 'rounded-2xl'];
const shadows = ['shadow-sm', 'shadow', 'shadow-md', 'shadow-lg'];

export function Spacing() {
  return (
    <div className="space-y-10">
      <header>
        <h1 className="text-2xl font-semibold">Spacing, Radius & Shadow</h1>
      </header>

      <section>
        <h2 className="mb-4 text-xl font-medium">Spacing scale (Tailwind defaults)</h2>
        <div className="space-y-2">
          {spacingSteps.map((n) => (
            <div key={n} className="flex items-center gap-3 text-sm">
              <code className="w-12 text-muted-foreground">p-{n}</code>
              <div className={`bg-primary h-3 w-${n}`} />
              <span className="text-muted-foreground">{n * 0.25}rem</span>
            </div>
          ))}
        </div>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Radius</h2>
        <div className="flex flex-wrap gap-4">
          {radii.map((r) => (
            <div key={r} className="flex flex-col items-center gap-2">
              <div className={`bg-primary h-16 w-16 ${r}`} />
              <code className="text-xs text-muted-foreground">{r}</code>
            </div>
          ))}
        </div>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Shadow</h2>
        <div className="flex flex-wrap gap-6">
          {shadows.map((s) => (
            <div key={s} className="flex flex-col items-center gap-2">
              <div className={`bg-card h-20 w-32 rounded-md ${s}`} />
              <code className="text-xs text-muted-foreground">{s}</code>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}
```

- [ ] **Step 2: Create the Motion page**

Create `ProjectCeres.Client/src/design-system/pages/Motion.tsx`:

```tsx
import { useState } from 'react';
import { Button } from '@/components/ui/button';

const tokens = [
  { name: '--motion-duration-fast', value: '120ms' },
  { name: '--motion-duration-base', value: '180ms' },
  { name: '--motion-duration-slow', value: '260ms' },
  { name: '--motion-easing-standard',   value: 'cubic-bezier(0.2, 0, 0, 1)' },
  { name: '--motion-easing-emphasized', value: 'cubic-bezier(0.3, 0, 0, 1)' },
];

export function Motion() {
  const [on, setOn] = useState(false);
  return (
    <div className="space-y-10">
      <header>
        <h1 className="text-2xl font-semibold">Motion</h1>
        <p className="mt-2 text-muted-foreground">
          Use the duration and easing tokens for any transition. Never inline a
          numeric duration in a component.
        </p>
      </header>

      <section>
        <h2 className="mb-4 text-xl font-medium">Tokens</h2>
        <table className="w-full max-w-xl text-sm">
          <tbody>
            {tokens.map((t) => (
              <tr key={t.name} className="border-b border-border">
                <td className="py-2 font-mono text-xs">{t.name}</td>
                <td className="py-2 text-muted-foreground">{t.value}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Demo</h2>
        <Button onClick={() => setOn((v) => !v)}>Toggle</Button>
        <div className="mt-4 h-16 w-48 overflow-hidden rounded-md border border-border bg-muted">
          <div
            className="h-full bg-primary"
            style={{
              width: on ? '100%' : '20%',
              transitionProperty: 'width',
              transitionDuration: 'var(--motion-duration-base)',
              transitionTimingFunction: 'var(--motion-easing-standard)',
            }}
          />
        </div>
      </section>
    </div>
  );
}
```

- [ ] **Step 3: Create the Charts page**

Create `ProjectCeres.Client/src/design-system/pages/Charts.tsx`:

```tsx
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, XAxis, YAxis } from 'recharts';
import { SwatchGrid, type Swatch } from '../components/SwatchGrid';

const chartSwatches: Swatch[] = Array.from({ length: 8 }, (_, i) => ({
  token: `chart-${i + 1}`,
  label: `Series ${i + 1}`,
  contrastAgainst: 'background',
}));

const sample = [
  { month: 'Jan', a: 400, b: 240, c: 180 },
  { month: 'Feb', a: 320, b: 300, c: 220 },
  { month: 'Mar', a: 500, b: 280, c: 260 },
  { month: 'Apr', a: 470, b: 350, c: 300 },
];

export function Charts() {
  return (
    <div className="space-y-10">
      <header>
        <h1 className="text-2xl font-semibold">Chart Palette</h1>
        <p className="mt-2 text-muted-foreground">
          Eight qualitative colors derived from the brand palette, all WCAG AA
          against the background in both light and dark modes.
        </p>
      </header>

      <section>
        <h2 className="mb-4 text-xl font-medium">Swatches</h2>
        <SwatchGrid swatches={chartSwatches} />
      </section>

      <section>
        <h2 className="mb-4 text-xl font-medium">Sample bar chart</h2>
        <div className="h-72 w-full max-w-2xl">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={sample}>
              <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" />
              <XAxis dataKey="month" stroke="var(--muted-foreground)" />
              <YAxis stroke="var(--muted-foreground)" />
              <Bar dataKey="a" fill="var(--chart-1)" />
              <Bar dataKey="b" fill="var(--chart-2)" />
              <Bar dataKey="c" fill="var(--chart-3)" />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </section>
    </div>
  );
}
```

- [ ] **Step 4: Create the Components page**

Create `ProjectCeres.Client/src/design-system/pages/Components.tsx`:

```tsx
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip';

export function Components() {
  return (
    <TooltipProvider>
      <div className="space-y-10">
        <header>
          <h1 className="text-2xl font-semibold">Components</h1>
          <p className="mt-2 text-muted-foreground">
            shadcn/ui primitives in every state. Add new primitives as later
            plans introduce them.
          </p>
        </header>

        <section>
          <h2 className="mb-4 text-xl font-medium">Buttons</h2>
          <div className="flex flex-wrap gap-3">
            <Button>Default</Button>
            <Button variant="secondary">Secondary</Button>
            <Button variant="outline">Outline</Button>
            <Button variant="ghost">Ghost</Button>
            <Button variant="link">Link</Button>
            <Button variant="destructive">Destructive</Button>
            <Button disabled>Disabled</Button>
          </div>
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Badges</h2>
          <div className="flex flex-wrap gap-3">
            <Badge>Default</Badge>
            <Badge variant="secondary">Secondary</Badge>
            <Badge variant="outline">Outline</Badge>
            <Badge variant="destructive">Destructive</Badge>
          </div>
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Card</h2>
          <Card className="max-w-md">
            <CardHeader>
              <CardTitle>Card title</CardTitle>
            </CardHeader>
            <CardContent>Card body content.</CardContent>
          </Card>
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Tabs</h2>
          <Tabs defaultValue="one" className="max-w-md">
            <TabsList>
              <TabsTrigger value="one">One</TabsTrigger>
              <TabsTrigger value="two">Two</TabsTrigger>
              <TabsTrigger value="three">Three</TabsTrigger>
            </TabsList>
            <TabsContent value="one">Tab one panel.</TabsContent>
            <TabsContent value="two">Tab two panel.</TabsContent>
            <TabsContent value="three">Tab three panel.</TabsContent>
          </Tabs>
        </section>

        <section>
          <h2 className="mb-4 text-xl font-medium">Tooltip</h2>
          <Tooltip>
            <TooltipTrigger asChild>
              <Button variant="outline">Hover me</Button>
            </TooltipTrigger>
            <TooltipContent>Tooltip content here</TooltipContent>
          </Tooltip>
        </section>
      </div>
    </TooltipProvider>
  );
}
```

- [ ] **Step 5: Build**

Run: `pnpm --dir ProjectCeres.Client build`
Expected: PASS — both `index.html` and `design-system.html` are emitted under `dist/`.

- [ ] **Step 6: Run dev server and verify visually**

Run: `pnpm --dir ProjectCeres.Client dev`
Open `http://localhost:5173/design-system.html` and verify:
- Sidebar with 7 nav items
- Theme toggle flips light/dark — every page responds
- Colors page shows surface, brand, semantic swatches with contrast ratios
- Typography page shows the Inter scale and the IBM Plex Mono table
- Spacing page shows spacing/radius/shadow scales
- Motion page shows the toggle demo
- Charts page shows 8 chart swatches and a working bar chart
- Components page shows buttons, badges, card, tabs, tooltip

Stop the server.

- [ ] **Step 7: Run all tests**

Run: `pnpm --dir ProjectCeres.Client test`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add ProjectCeres.Client/design-system.html ProjectCeres.Client/vite.config.ts ProjectCeres.Client/src/design-system
git commit -m "feat(design-system): add /design-system showcase route"
```

---

## Task 10: Write `docs/design-system.md`

**Files:**
- Create: `docs/design-system.md`

- [ ] **Step 1: Create the document**

Create `docs/design-system.md` with the following content:

```markdown
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
9. [Showcase route](#showcase-route)

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
- **Inter** (`--font-sans`) — all UI text and prose. Variable font for size flexibility.
- **IBM Plex Mono** (`--font-mono`) — currency, percentages, and dates in **tabular contexts only**.

**The rule for percentages:**
- In **data contexts** (KPI cards, table cells, chart axes): use mono via `<Numeric>`.
- In **prose** (sentences with embedded figures): use the surrounding font (Inter). Switching mid-sentence is jarring and harms readability.

The `<Numeric>` component (see below) is the enforcement mechanism — wrap any tabular numeric in it; never apply `font-mono` directly.

**Type scale:** Tailwind defaults — `text-xs` (12px) through `text-4xl` (36px). See the live scale at `/design-system.html#/typography`.

---

## Spacing, radius, shadow

- **Spacing:** Tailwind defaults (4px increments). No custom scale.
- **Radius:** `--radius: 0.625rem` (10px) is the base. Tailwind's `rounded-sm/md/lg/xl/2xl/3xl/4xl` derive from it.
- **Shadow:** four steps — `shadow-sm`, `shadow`, `shadow-md`, `shadow-lg`. Dark-mode shadows are darker because they sit on dark surfaces.

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

## Showcase route

`http://localhost:5173/design-system.html` (dev) — renders every token and shadcn primitive in every state, with light/dark toggle. Open it whenever a token changes; visual regressions show up here first.
```

- [ ] **Step 2: Commit**

```bash
git add docs/design-system.md
git commit -m "docs(design-system): add design system reference"
```

---

## Task 11: Mark `planning-phase3.md` §6 Brand Foundation as resolved

**Files:**
- Modify: `docs/planning-phase3.md`

- [ ] **Step 1: Replace §6 Brand Foundation with a pointer**

Find the section starting `### 6. Brand foundation` and ending before `### 7. Dark mode`. Replace its body (the bullet list of color/typography/spacing/etc.) with:

```markdown
### 6. Brand foundation

**Status: Resolved.** All brand tokens — color palette, typography (Inter + IBM Plex Mono), spacing, radius, shadow, motion, chart palette, shadcn override list — are defined in [`docs/design-system.md`](design-system.md) and live in `ProjectCeres.Client/src/index.css`. The internal `/design-system.html` route renders every token for visual reference.
```

Leave §7 Dark mode and onward untouched.

- [ ] **Step 2: Commit**

```bash
git add docs/planning-phase3.md
git commit -m "docs(planning-phase3): mark Brand Foundation resolved; link design-system.md"
```

---

## Task 12: Final verification

- [ ] **Step 1: Full client build**

Run: `pnpm --dir ProjectCeres.Client build`
Expected: PASS — `dist/index.html` and `dist/design-system.html` both produced.

- [ ] **Step 2: Full client tests**

Run: `pnpm --dir ProjectCeres.Client test`
Expected: PASS.

- [ ] **Step 3: Full .NET build**

Run: `dotnet build`
Expected: PASS — Razor app still builds; CSS pipeline still works.

- [ ] **Step 4: Manual smoke test of existing Razor pages**

Run: `dotnet run --project ProjectCeres`
Open the Razor app in a browser and verify:
- Existing pages still render
- React-island components (navbar, charts, badges, switches) still appear
- Token changes are visible: primary buttons are teal, fonts are Inter, charts use the new palette

Stop the server.

- [ ] **Step 5: Manual smoke test of `/design-system.html`**

Run: `pnpm --dir ProjectCeres.Client dev`
Open `http://localhost:5173/design-system.html` and click through every nav item. Toggle light/dark. Confirm every page renders without console errors.

Stop the server. No commit needed — verification only.

---

## Out of scope (future plans)

- App shell (sidebar, top bar, responsive behavior) — next plan
- Auth screens — separate plan
- Onboarding wizard — separate plan
- Dashboard SPA port — separate plan
- Movements/Transactions/Transfers SPA port — separate plan
- Adding remaining shadcn primitives (Sheet, Breadcrumb, Avatar, Skeleton, Sonner, Command, DataTable) — added incrementally as the plans that need them are written
- Accessibility tooling (`eslint-plugin-jsx-a11y`, `vitest-axe`) — wired in alongside the app shell plan
- React Router for the production app — only mounted on `/design-system.html` here; the production-app router setup belongs to the App Shell plan
