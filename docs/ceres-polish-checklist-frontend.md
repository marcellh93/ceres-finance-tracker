# Project Ceres — Unified Polish Checklist

Status markers used throughout:

- ✅ **Done** — already in your codebase, file/line referenced
- ⚠️ **Partial** — present but with gaps; what to fix is noted
- ❌ **TODO** — not present; what to add is noted
- ❓ **Unverifiable** — depends on a file not uploaded
- **[E]** Essential / **[P]** Polish (severity from the original checklist)

---

## TL;DR

Ceres is in better shape than most production apps. The hard parts — motion tokens, skeleton heights, view-transition naming, route-focus convention, validation patterns, sonner contrast tinting — are already correct. What's left is a small number of CSS additions and a handful of consistency fixes. **Tier 1 below (six items) gives ~80% of the remaining "feels well done" jump.**

## Key Findings

1. **`next-themes` is installed but `ThemeToggle.tsx` doesn't use it.** Wiring it in solves FOUC, theme persistence, and system-preference detection in one move.
2. **The Switch thumb snaps.** `index.css` lines 65–68 set the translated position via `data-checked`/`data-unchecked` but include no `transition` rule.
3. **`QuickAddModal.tsx` violates the Money input recipe.** Uses `type="number" step="0.01"` instead of the `type="text" inputMode="decimal"` raw/display/wire pattern that `MovementForm.tsx` correctly uses. Real bugs: mousewheel changes value; locale-aware decimal separators broken.
4. **`index.css` has no `prefers-reduced-motion` rule and no `::view-transition-old/new(*)` defaults wired to your motion tokens.** Two missing CSS rules — one for accessibility, one for transition consistency.
5. **`AppLayout.tsx` has no `<ScrollRestoration />`** — back-navigation loses scroll position.
6. **`index.html` is bare** — no FOUC guard, no `theme-color` meta, no per-route title strategy.
7. **Manual chunks already configured** in `vite.config.ts`. `tw-animate-css` already imported. View-transition naming convention already wired in `MovementForm.tsx` line 393. These were question marks; they're done.

---

## Details — The Checklist

### 0. Foundation: Motion design tokens

- 0.1 [E] ✅ Duration + easing tokens defined. `index.css` lines 109–113. `--motion-duration-fast: 120ms / base: 180ms / slow: 260ms`, plus standard and emphasized easings. On `:root` only — intentional, motion doesn't theme.
- 0.2 [E] ✅ `tw-animate-css` imported. `index.css` line 2.
- 0.3 [P] ❌ `@formkit/auto-animate` not installed. Add for list reorders (Movements table, AttachmentDropzone file list).
- 0.4 [P] — `motion` (Framer Motion) not needed yet. Don't install until you have a concrete need View Transitions can't cover.

### 1. Page transitions

- 1.1 [E] ⚠️ `viewTransition` prop on `<Link>`. Cannot verify in `Sidebar.tsx` (not uploaded). Showcase `App.tsx` lines 36–46 lacks it.
- 1.2 [E] ✅ `::view-transition-old/new(root)` defaults shipped (commit `a192c1d`) — `animation-duration: var(--motion-duration-base)` + `animation-timing-function: var(--motion-easing-standard)` + reduced-motion guard. Dormant until navigation opts in via `<Link viewTransition>` or `document.startViewTransition()`. See 1.1.
- 1.3 [P] ✅ Shared-element view transition naming. Convention documented + applied (`MovementForm.tsx` line 393).
- 1.4 [E] ❓ `startTransition` for programmatic navigation. Depends on Sidebar/router code.
- 1.5 [E] ✅ Focus reset on route change (substitutes for live-region announcer). `PagePlaceholder.tsx` and `MovementForm.tsx` both focus `<h1>` on mount via `tabIndex={-1}` + `outline-none` + ref/useEffect.
- 1.6 [E] ✅ Skip link. `AppLayout.tsx` lines 18–23.
- 1.7 [P] ❌ Per-route `document.title`. `index.html` static title is `projectceres-client`. Add a `useDocumentTitle(title)` hook or layout-level `useLocation()` updater.

### 2. Skeleton loaders matching real heights

- 2.1 [E] ✅ Skeleton heights match content. `h-[220px]` charts, `h-[400px]` tables, `h-5 w-32` text. `MovementForm.tsx` line 273 even preserves the `id` for `htmlFor` continuity during loading.
- 2.2 [P] — Skeleton height tokens. Currently arbitrary Tailwind values; tokens would centralize but isn't blocking.
- 2.3 [E] ✅ Skeletons co-located with components.
- 2.4 [E] ❓ Image aspect-ratio reservations. Not yet visible; verify wherever `<img>` appears.
- 2.5 [E] ✅ shadcn `<Skeleton>` used everywhere.
- 2.6 [P] — Shimmer vs pulse. Pulse is fine for base-nova.
- 2.7 [E] ✅ `useDelayedLoading(loading, options?)` shipped at `src/app/lib/use-delayed-loading.ts` (default 150ms). Used by every `<DataTransition>`-wrapped surface across the SPA after Stage 5.4 rollout. Fast responses no longer flash a skeleton.
- 2.8 [E] ✅ DataTransition's skeleton slot is wrapped in `role="status" aria-busy="true" aria-live="polite"` with a configurable `loadingLabel` prop (default `"Loading"`). The status region lives at the wrapper, not the leaf `<Skeleton>` primitive — putting it on each shimmer would create N nested status regions for grouped skeletons. (commit `55d2030`)
- 2.9 [P] ❓ Suspense + skeleton vs conditional rendering. Depends on routes/data layer.

### 3. Microinteractions and animations

- 3.1 [E] ⚠️ Hover transitions exist but use Tailwind literals (`duration-200`). Migration to motion tokens already on your roadmap (Known Limitation in `design-system.md` line 1154).
- 3.2 [E] ❓ Button active/press state. Verify `components/ui/button.tsx` includes `active:scale-[0.98]` or equivalent.
- 3.3 [E] ✅ Focus-visible rings consistent. Global `* { outline-ring/50 }` rule in `index.css` line 170.
- 3.4 [E] ⚠️ `transition-colors` used correctly mostly. Status block (`MovementForm.tsx` lines 460/469/483/491) uses literal `duration-200` — same migration as 3.1.
- 3.5 [E] ✅ Dialog/Sheet/Dropdown shadcn defaults preserved. Sonner toast tinting is icon-only — preserves AA contrast on body text.
- 3.6 [P] ❌ List stagger animations. Add `@formkit/auto-animate`.
- 3.7 [E] ✅ Lucide icon sizing consistent. `h-3/4/5 w-3/4/5` used appropriately, `aria-hidden="true"` set with adjacent text.
- 3.8 [E] ✅ No `animate-bounce`/`animate-spin` on idle UI.
- 3.9 [P] — `ease-out` for entrances vs `ease-in` for exits. shadcn defaults are fine.
- 3.10 [E] ✅ Switch thumb animates `translate` with `--motion-duration-base` + `--motion-easing-standard` (commit `481e045`). The thumb position is set via the standalone `translate` property, not `transform`, so the original `transition-transform` className was a no-op.

### 4. Reduced motion and accessibility

- 4.1 [E] ✅ Global `@media (prefers-reduced-motion: reduce)` rule shipped (commit `b38e473`). Collapses every animation/transition to ~instant by setting `animation-duration: 0.01ms` + `animation-iteration-count: 1` + `transition-duration: 0.01ms` + `scroll-behavior: auto` on `*, *::before, *::after`. Catches third-party libs (tw-animate-css, sonner spinner, base-ui dialog enters) without per-call wiring.
- 4.2 [E] ✅ View-transition root rule has its own reduced-motion guard (commit `a192c1d`) that sets `animation: none` — stricter than the global 0.01ms; redundant but explicit.
- 4.3 [E] — Autoplay animations >5s. None visible.
- 4.4 [E] ❓ Keyboard nav full coverage. Trust Base UI primitives; verify with vitest-axe (4.6).
- 4.5 [E] ✅ Skip link. `AppLayout.tsx` lines 18–23.
- 4.6 [E] ❌ `vitest-axe` not installed. Add and run against `MovementForm`, `QuickAddModal`, `AppLayout` minimum.

### 5. Perceived performance

- 5.1 [E] ❌ `useOptimistic` not used. Highest-leverage place: clearing/unclearing a transaction directly from the table row.
- 5.2 [P] — `useDeferredValue`. Apply if Movements filter has perf issues at >100 rows.
- 5.3 [E] ❓ `React.lazy` route-based splitting. Depends on `routes.tsx`.
- 5.4 [E] ✅ Manual chunks configured. `vite.config.ts` lines 41–47 split `vendor-react`, `vendor-charts`, `vendor-ui`.
- 5.5 [E] ❌ No bundle visualizer / size budget. Add `rollup-plugin-visualizer` (dev) + CI size check on `dist/assets/*.js`.
- 5.6 [E] ❓ Hover prefetch on `<Link>`. Depends on Sidebar/router.
- 5.7 [E] ❓ `<img loading="lazy" decoding="async">`. Verify wherever images appear.
- 5.8 [E] ❓ LCP image preload. Depends on what's above the fold.
- 5.9 [E] ✅ Fonts use `font-display: swap` via fontsource defaults.
- 5.10 [P] — Font fallback metric matching. Optional.
- 5.11 [E] ✅ No render-blocking 3rd-party scripts. Confirmed by `index.html`.

### 6. Smoothness fundamentals (rendering pipeline)

- 6.1 [E] ⚠️ Mostly compliant; two violations in the showcase. `Spacing.tsx` line 20 and `Motion.tsx` lines 47–53 transition `width`. Static so no jank, but the Motion demo specifically teaches the wrong technique. Refactor to `transform: scaleX(...)` with `transform-origin: left`.
- 6.2 [E] ❓ Accordion/collapsible technique. Verify any custom collapsibles use `grid-template-rows: 0fr→1fr`, not `height: auto`.
- 6.3 [P] ❓ `will-change` usage. Grep for it; should be at-animation-time only.
- 6.4 [E] ❓ Debounce/throttle scroll & resize. Depends on scroll handlers.
- 6.5 [E] ❓ List virtualization >100 rows. Movements table likely benefits.
- 6.6 [P] — Mid-range device testing. Manual.

### 7. State transitions and data fetching

- 7.1 [E] ❓ Suspense boundaries at loading regions. Depends on route + island structure.
- 7.2 [E] ❓ Error boundaries wrapping Suspense. Pair with `react-error-boundary` if absent.
- 7.3 [E] ❓ Stale-while-revalidate. `useApi` hook implementation not visible.
- 7.4 [P] — Progressive partial data. Pattern question.
- 7.5 [E] ✅ Loader delay shipped via `useDelayedLoading` (150ms default) — see 2.7.

### 8. Form and input feel

- 8.1 [E] ✅ Visible focus state on inputs. shadcn defaults + global ring rule.
- 8.2 [E] — Validation timing. Currently submit-only (server 422 round-trip). Acceptable; on-blur is a future improvement.
- 8.3 [E] ✅ Error messages render inline, don't snap.
- 8.4 [E] ❌ Submit button states are idle/loading only. Build shared `<SubmitButton>` with idle/loading/success/error + spinner. Replaces `QuickAddModal.tsx` line 230 and `MovementForm.tsx` lines 548–550.
- 8.5 [P] ❌ `useOptimistic` for inline auto-save. Same as 5.1.
- 8.6 [E] ✅ `<MoneyInput>` extracted at `src/app/components/MoneyInput.tsx` and consumed by MovementForm + all three QuickAddModal amount fields. QuickAdd's `type="number"` locale bug is fixed. design-system.md "Money input" section rewritten as component usage docs. (commit `496f1e3`)
- 8.7 [E] ✅ `<Field>` extracted at `src/app/components/Field.tsx`. QuickAddModal, MovementForm, and RecurringForm migrated (the third Field implementation, missed by the original audit). (commit `fa6c0d7`)

### 9. Scrolling and navigation

- 9.1 [E] ✅ Custom `useScrollRestoration(mainRef)` hook wired in `AppLayout.tsx`. Restores `<main>` scrollTop on browser back across every `/app/*` route. The framework `<ScrollRestoration />` component doesn't fit — it's data-router-only and watches `window`, while this app uses `<BrowserRouter>` and scrolls via `<main>`. Hook lives at `src/app/lib/use-scroll-restoration.ts`; persists positions in sessionStorage keyed by pathname.
- 9.2 [E] ❓ Smooth anchor scroll with reduced-motion guard. Verify `html { scroll-behavior }`.
- 9.3 [P] ✅ Sticky header pattern is documented + correct (no animating `top`/`transform`).
- 9.4 [P] ❓ `scroll-margin-top` for in-page anchors. Verify if any.

### 10. Color, theme, and visual polish

- 10.1 [E] ⏸ Theme switch is a hard flip. **Tested a global `transition-colors` rule on `*, *::before, *::after`; reverted because it made every hover/focus feel laggy** (it animates all color changes, not just theme flips). Snap is the production default in Vercel/Linear/GitHub. If a smooth flip is wanted later, the cleaner path is wrapping `setTheme()` in `document.startViewTransition()` so the root `::view-transition-old/new(root)` rule (already shipped) handles the cross-fade browser-side.
- 10.2 [E] ✅ `next-themes` `<ThemeProvider>` mounted in both SPA and design-system entrypoints (commit `1a14118`); ThemeToggle in TopBar + mobile drawer. FOUC handled by next-themes' inline script. `attribute="class"`, `defaultTheme="system"`, `enableSystem`, `disableTransitionOnChange` (latter pairs with the deferred 10.1).
- 10.3 [E] ✅ Border radius consistent. All `--radius-*` derived from `--radius: 0.625rem`.
- 10.4 [P] ✅ Shadow scale 4 tiers.
- 10.5 [E] ✅ Semantic color tokens used everywhere. No `bg-zinc-100`-style raw tokens.
- 10.6 [E] ✅ OKLCH everywhere.

### 11. shadcn/ui base-nova specifics

- 11.1 [E] ✅ `style: base-nova`, `cssVariables: true`.
- 11.2 [E] ✅ No wrapping padding around shadcn components.
- 11.3 [E] ✅ Customization via component file edits, not global overrides.
- 11.4 [E] ✅ `data-state` attributes used for state-driven styling.
- 11.5 [P] ✅ Lucide icon sizing consistent.

### 12. Testing animations and transitions

- 12.1 [E] ❌ Animations not disabled in tests. `test-setup.ts` mocks DOM APIs thoroughly but doesn't intercept CSS transitions or `Element.prototype.animate`. Add a global CSS override or stub `animate`.
- 12.2 [E] ✅ Tests assert behavior, not animation specifics.
- 12.3 [E] ❌ No reduced-motion test path. matchMedia mock returns `matches: false` always. Add a helper that mocks reduced-motion = true.
- 12.4 [P] — Visual regression testing. Optional; skip for now.
- 12.5 [E] ❌ No `vitest-axe`. Same as 4.6.

### 13. Code quality and consistency

- 13.1 [E] ❓ Tailwind class ordering (`prettier-plugin-tailwindcss`). Not visible in `package.json` shown — verify and add if absent.
- 13.2 [E] ⚠️ Transition utilities token-referenced — partially. `duration-200` literals tracked for migration.
- 13.3 [E] ⚠️ Reusable transition strings. Status block has 4 lines all `transition-colors duration-200`. Extract a constant or component variant.
- 13.4 [P] — Animated component naming convention. Not blocking.
- 13.5 [E] ❓ `tsconfig` strictness. Cannot verify without `tsconfig.json`.

### 14. Library decision tree

- 14.1 [E] ✅ `tw-animate-css` installed.
- 14.2 [P] ❌ `@formkit/auto-animate` not installed. Recommend.
- 14.3 [E] ✅ `motion` not needed. Don't install.
- 14.4 — GSAP not needed.
- 14.5 — `react-spring` not needed.
- 14.6 [P] — `spin-delay` (or hand-rolled `useDelayedLoading`). Same as 2.7.

---

## 15. The "feels well done" gut-check

Walk the app and check each:

- [x] No element appears or disappears instantly except in response to typing. (Stage 5.4 cross-fade ships across the SPA via DataTransition)
- [x] No content jumps when data loads — skeleton heights match reality.
- [~] Hovering any button gives visible feedback within 150ms (uses `duration-200`, fine).
- [ ] Pressing any button gives a subtle scale/color change. (verify Button primitive — Tier 3)
- [x] Tab key reveals a clear focus ring on every interactive element.
- [~] Switching themes is smooth, not flashy. (10.2 shipped; 10.1 deliberately not shipped — flip is a clean snap)
- [~] Navigating between pages cross-fades, doesn't snap. (1.2 root rule shipped, dormant until navigation opts in via `<Link viewTransition>` — Tier 3 follow-up)
- [x] Submitting a form shows immediate feedback. (T3.12 SubmitButton shipped 2026-05-08)
- [x] No spinners flash for <200ms. (2.7 `useDelayedLoading` shipped + Stage 5.4 rollout)
- [x] All icons sized identically in similar contexts.
- [x] Border radii consistent.
- [x] In Reduce Motion mode, the app still works and animations are subdued. (4.1 global override + 2.8 status region)
- [x] Switch toggle slides smoothly. (3.10 — `transition: translate` on `--motion-duration-base`)

Legend: `[x]` already correct, `[~]` partial, `[ ]` to fix.

---

## Tier-ordered action list

Sequencing for handing to Claude Code. Tiers are independent — finish one before starting the next.

**Tier 1 — six small CSS / one library wire-up** (all shipped except T1.5 deliberately deferred):

1. ✅ Wire `next-themes` into `ThemeToggle.tsx` (10.2, 10.1 partial) — commit `1a14118`
2. ✅ Switch thumb transition rule on `translate` (3.10) — commit `481e045`
3. ✅ `::view-transition-old/new(root)` defaults (1.2) — commit `a192c1d`
4. ✅ Global `prefers-reduced-motion: reduce` override (4.1, 4.2) — commit `b38e473`
5. ⏸ ~~Global theme-flip `transition-colors` rule (10.1)~~ — **tested and reverted** (made hovers laggy); see 10.1 for the View Transitions API alternative
6. ✅ Custom `useScrollRestoration(mainRef)` wired in `AppLayout.tsx` (9.1) — commit `de5e919`

**Tier 2 — extract shared primitives** (all shipped):

7. ✅ `<Field>` extracted (`src/app/components/Field.tsx`, commit `fa6c0d7`)
8. ✅ `<MoneyInput>` extracted (`src/app/components/MoneyInput.tsx`, commit `496f1e3`) — fixes the QuickAdd `type="number"` locale bug
9. ✅ Skeleton ARIA via `DataTransition` `role="status"` wrapper (commit `55d2030`)
10. ✅ `useDelayedLoading` shipped earlier under Stage 5.2 (`src/app/lib/use-delayed-loading.ts`)

**Tier 3 — production-app polish:** ✅ all shipped 2026-05-08

11. ✅ Migrated `duration-200` literals to motion tokens (commit `c372b89`); Tabs primitive bound to `--motion-duration-base` in follow-up `014f7e3`
12. ✅ `<SubmitButton>` with idle/loading/success/error + spinner (commit `8b4c731`); QuickAddModal-only consumer; success-flash dwell tuned to 1500 ms in `d462eec` after research
13. ✅ `useOptimistic` for the Status block toggle (commit `0c5c1b0`); spec corrected with `pendingRef` requirement (`52d2c17`)
14. ✅ MovementForm budget `<select>` replaced with `BudgetCombobox` (commit `1394db6`); clear-✕ structural fix in `eeb0c13`/`d3702bd`/`182dd08`
15. ✅ HTML entries hardened — `theme-color` + description meta + per-route titles via `useDocumentTitle` (commit `d828161`); served-HTML correction (Razor shells) in `ff362fc`/`de28b8a`; favicon wired in `54a8451`

**Tier 4 — testing and observability:** ✅ all shipped 2026-05-08

16. ✅ Disabled CSS animations in `test-setup.ts` (commit `9f61771`)
17. ✅ `vitest-axe` with `expectNoA11yViolations` helper, gating on serious + critical severities; coverage on MovementForm, QuickAddModal, AppLayout (commit `e96d898`)
18. ✅ Bundle visualizer (`rollup-plugin-visualizer`) emits `dist/stats.html` on every build; per-asset gzip budgets enforced via `pnpm check-size` chained into `pnpm build` (commit `c37f4eb`)

**Tier 5 — discretionary:** ✅ T5.20/T5.21 shipped 2026-05-09; T5.19 deferred

19. ⏸ `@formkit/auto-animate` for lists that reorder — **deferred 2026-05-09: no candidate consumer.** Reopens when a real reordering surface emerges.
20. ✅ OKLCH support in `parseRgb()` (commit `17d6e93`); SwatchGrid try/catch fallback removed.
21. ✅ Motion rule indexed in design-system.md Working rules as rule 7 (commit `37f55fe`).

---

## Caveats

- **Couldn't verify:** `Sidebar.tsx`, `TopBar.tsx`, `routes.tsx`, `tsconfig.json`, the Movements table, the `useApi` hook, `components/ui/button.tsx`, `components/ui/skeleton.tsx`. Items marked ❓ depend on these.
- **`next-themes` mystery:** Installed but not imported in `ThemeToggle.tsx`. May be used elsewhere; if not, it's a stale dep that happens to be the right solution to several gaps.
- **Motion token migration is acknowledged.** `duration-200` literals are already tracked in `design-system.md` Known Limitations. Flagged here for completeness, not as new news.
- **180ms `--motion-duration-base` is on the snappy end** of the industry range (vs typical 200ms). Defensible — fast UIs feel responsive — just a deliberate aesthetic choice if anyone questions it.
- **`type="number"` issue is bigger than it looks.** Beyond UX issues (mousewheel, spinners), locale-aware decimal separators don't work — a Spanish user with `comma_decimal` settings can't enter `1,50` correctly in QuickAddModal. Real i18n bug, not just polish.
- **Tier 1 alone gets you most of the visible payoff.** If time is tight, do those six and stop.
