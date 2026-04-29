# Spec: App Shell

> **Date:** 2026-04-29
> **Phase:** 3
> **Status:** Approved — ready for implementation planning
> **Predecessors:** [SPA Migration + UX/UI Overhaul](2026-04-28-spa-migration-ux-overhaul-design.md), [Design System Foundation](../plans/2026-04-29-design-system-foundation.md)

The App Shell is the second deliverable of the SPA migration. It establishes the layout chrome (left sidebar, top bar, responsive behaviour) inside which every future page will be rendered. This spec covers chrome only — search, quick-add, notifications, and avatar dropdown items render as visible-but-stubbed UI; their real behaviour ships in follow-up plans alongside the backend and feature work that supports them.

---

## Index

1. [Architecture](#1-architecture)
2. [Layout primitives](#2-layout-primitives)
3. [Sidebar](#3-sidebar)
4. [Top bar widgets (stubs)](#4-top-bar-widgets-stubs)
5. [Routing & placeholder pages](#5-routing--placeholder-pages)
6. [Responsive behaviour](#6-responsive-behaviour)
7. [Accessibility baseline](#7-accessibility-baseline)
8. [Testing strategy](#8-testing-strategy)
9. [Out of scope](#9-out-of-scope)

---

## 1. Architecture

### Hosting model

A new Vite entry point `app.html` is added alongside `index.html` (Razor islands) and `design-system.html` (showcase). ASP.NET Core gains an `AppController` whose `Index` action returns a single Razor view (`Views/App/Index.cshtml`) that hosts `<div id="root">` and references the `app` entry's compiled JS + CSS. A catch-all route maps every path under `/app/{*path}` to the same controller action so React Router (`BrowserRouter`, `basename="/app"`) handles all sub-paths client-side.

### Why BrowserRouter (not HashRouter)

The design-system showcase uses HashRouter because it's a static file with no server cooperation. The shell *does* have server cooperation (the catch-all route), so we get clean URLs without `#` and bookmarkable deep links work without a fragment.

### Migration URL strategy

During migration: SPA at `/app/*`, Razor at existing paths (`/Dashboard`, `/Movements`, …). Each migrated feature: the corresponding Razor controller starts redirecting (`/Dashboard` → `/app/dashboard`). Final cleanup (last plan in the migration sequence): the `/app/` prefix is dropped, SPA serves `/`, Razor catch-alls deleted. That cleanup is its own focused plan.

### Token reuse

The shell consumes the design system from the foundation plan: same `index.css`, same `<Numeric>`, same shadcn primitives. No new tokens. Brand teal shows up in the active nav-link state, the focus ring, the Sprout icon, and the primary button.

### File boundary

All shell code lives under `ProjectCeres.Client/src/app/`, mirroring the `src/design-system/` pattern:

```
src/app/
├── main.tsx              # React entry — BrowserRouter, basename "/app"
├── App.tsx               # Router root with all routes
├── layout/
│   ├── AppLayout.tsx     # CSS Grid: top bar + (sidebar + outlet)
│   ├── Sidebar.tsx       # Desktop/tablet sidebar (expanded or rail)
│   ├── TopBar.tsx        # Brand area + search + widgets
│   ├── MobileDrawer.tsx  # Slide-over for <640px
│   ├── AvatarMenu.tsx    # User dropdown (Profile / Security / Logout)
│   ├── BrandMark.tsx     # Sprout icon + "Ceres" wordmark
│   └── nav-items.ts      # Single source of truth for nav structure
├── components/
│   ├── PagePlaceholder.tsx
│   └── SearchModal.tsx   # Stub modal opened by ⌘K
├── pages/
│   └── *.tsx             # 16 placeholder routes (one-liners)
└── lib/
    ├── use-keyboard-shortcut.ts
    ├── use-media-query.ts
    └── sidebar-storage.ts
```

Production-bound `@/components/*` is shared with the showcase and Razor islands.

---

## 2. Layout primitives

### Shell shape

```
┌──────────────────────────────────────────────────────────────────┐
│  TopBar (h-14, fixed)                                            │
├────────────┬─────────────────────────────────────────────────────┤
│            │                                                     │
│  Sidebar   │  <Outlet/>  — page content scrolls here             │
│ (240px     │                                                     │
│  or 56px   │                                                     │
│  on rail)  │                                                     │
│            │                                                     │
└────────────┴─────────────────────────────────────────────────────┘
```

CSS Grid layout. Outer container: `grid-template-rows: 56px 1fr`. Nested grid below the top bar: `grid-template-columns: var(--sidebar-w) 1fr`. The CSS variable `--sidebar-w` toggles between `240px` (expanded), `56px` (rail), and `0` (mobile, drawer-only). Transition: `grid-template-columns var(--motion-duration-base) var(--motion-easing-standard)`.

### Why custom grid (not shadcn `Sidebar` primitive)

shadcn ships an opinionated `Sidebar` block with its own state machine, keyboard shortcuts, and styling assumptions. Our shell is small and the layout requirements are specific (rail width, exact transition tokens, hash-toggle behaviour). A ~100-line custom grid that consumes our existing motion tokens is simpler to reason about and easier to retune. shadcn primitives are still used for the *contents*: `DropdownMenu`, `Sheet`, `Button`, `Tooltip`, `Input`, `Dialog`, `Popover`, `Avatar`, `Separator`.

### Top bar layout

Single row, `h-14`, three flex sections:

- **Left:** brand area, width matches `--sidebar-w` so it visually aligns with the sidebar below. Contains the `Sprout` Lucide icon (color: `text-primary`) + "Ceres" wordmark in Inter Bold. Wordmark hidden in rail mode and on mobile.
- **Center:** shadcn `Input` with `Search` icon left, "⌘K" keyboard hint right, placeholder "Search transactions, accounts…". Click or `⌘K`/`Ctrl+K` opens the stub modal.
- **Right:** Quick-add `Plus` button (variant `default`, size `icon`); notifications `Bell` button (variant `ghost`, size `icon`); `AvatarMenu` trigger.

### Mobile top bar

Replaces the brand-area-aligned layout: hamburger left (opens drawer), wordmark centred, search icon + avatar right. Quick-add and notifications hide entirely on mobile (`<sm`) to save horizontal space.

---

## 3. Sidebar

### Nav structure (single source of truth)

```
Activity
  Movements                LayoutList
  Transactions             Receipt
  Transfers                ArrowLeftRight
  Review                   Inbox

Money
  Accounts                 Landmark
  Categories               Tags
  Budgets                  Wallet

Tools
  Recurring Transactions   Repeat
  Import                   Upload
  Reports                  BarChart3

(bottom-pinned)
  Settings                 Settings
  Support                  LifeBuoy
```

Three labelled groups separated by gaps; Settings + Support pinned to the bottom via `mt-auto`. The first item in the routing table (`/app/`, "Dashboard") is reached by clicking the brand mark in the top bar — it does not appear in the sidebar nav. (Rationale: the brand mark already conventionally returns to the home/overview; duplicating it in the sidebar adds one redundant item.)

### IA decisions resolved here

- **"Activity" group name** (not the spec's "Main") because it's descriptive of what's in it.
- **"Review"** is a single sidebar item under Activity — not its own group, not two separate Reconciliation entries — because reconciliation is a *cross-cutting status* on transactions/transfers, not a separate entity. The Review page renders a unified list of items needing attention (cleared toggles, transfer matches, etc.). This consolidates what the existing Razor navbar exposes via the "Pending Transfers / Pending Reconciliations" dropdown.
- **No "Preferences" entry in the avatar dropdown.** The spec lists it but it is the same thing as the "Settings" sidebar item under two names. Sidebar Settings remains; avatar dropdown drops "Preferences."

### Active state

- Background: `bg-accent` (pale teal)
- Text: `text-accent-foreground` (dark teal)
- Left bar: 2px brand-teal `box-shadow: inset 2px 0 0 var(--primary)`
- `aria-current="page"`

Hover (inactive): `bg-muted text-foreground`. Color is never the *only* differentiator (the left bar provides a non-color cue).

### Rail mode

Each nav item collapses to icon-only, centered. The label moves into a shadcn `Tooltip` (placement `right`) on hover/focus. Group labels disappear; the visual spacing between groups remains as a separator. Toggle button at the bottom of the rail flips between `ChevronsLeft` (when expanded) and `ChevronsRight` (when in rail).

### Persistence and pre-hydration

The collapse state persists to `localStorage` under key `ceres.sidebar.collapsed`. To prevent a visual flicker on first paint, an inline `<script>` in `Views/App/Index.cshtml` runs before React hydrates:

```html
<script>
  (function () {
    try {
      var c = localStorage.getItem('ceres.sidebar.collapsed');
      if (c === 'true') document.documentElement.classList.add('sidebar-collapsed');
    } catch (e) {}
  })();
</script>
```

CSS reads `--sidebar-w: 240px` by default and overrides to `56px` when `.sidebar-collapsed` is on `<html>`. React mirrors the same class on toggle. The same pattern will be reused later for dark-mode persistence.

### Mobile drawer

shadcn `Sheet` from the left side. Same nav contents (no toggle button — the drawer is always "expanded" when open). Tapping a nav item closes the sheet via `onOpenChange`. Tap-outside and Escape close it natively (Sheet behaviour, verified during implementation).

---

## 4. Top bar widgets (stubs)

Every interactive widget in the top bar is **visually present and functional as UI** but its underlying behaviour is stubbed. Each widget is wired so the visible pieces match the final design and only the data/handlers are placeholders.

### Search input

- shadcn `Input` with `Search` icon left and "⌘K" `kbd` hint right.
- Click or `⌘K` (Mac) / `Ctrl+K` (others) opens a shadcn `Dialog`. Modal shows a focused `Input` and an empty results area: *"Search is not yet wired up. The /api/search endpoint will be added in a follow-up plan."*
- Modal closes on Escape, click-outside, or `⌘K` again.
- Shortcut hook: `useKeyboardShortcut('mod+k', open)` at `src/app/lib/use-keyboard-shortcut.ts`. The "mod" abstraction handles Mac vs others and ignores the shortcut when an `<input>` or `<textarea>` is focused (so users typing `k` in form fields don't reopen the modal).
- Mobile: search icon button replaces the inline input; tapping it opens the same modal.

### Quick-add `+`

- `Plus` Lucide icon Button (variant `default`, size `icon`).
- Click opens a `Dialog`: *"Quick-add will let you record a transaction or transfer without leaving the page. Coming in a follow-up plan."*
- No keyboard shortcut yet.
- Hidden on mobile.

### Notifications bell

- `Bell` Lucide icon Button (variant `ghost`, size `icon`).
- Renders a small numeric `Badge` overlay when unread count > 0. **Hardcoded to 0** in this plan — the badge does not appear. Component takes a prop so the future notification system can drive it.
- Click opens a `Popover` with placeholder text: *"You have no notifications."*
- Hidden on mobile.

### Avatar dropdown

- shadcn `Avatar` with fallback initials "U" (no user data exists yet). Wrapped in a `DropdownMenu`.
- Items in order: Profile, Security, `Separator`, Logout.
- Profile/Security route to placeholder pages.
- Logout opens a `Dialog`: *"Logout will end your session. Wired up when authentication ships."*

### shadcn primitives this plan adds

`pnpm dlx shadcn add` for: `input`, `popover`, `dropdown-menu`, `avatar`, `sheet`, `separator`, `kbd`. (`dialog` and `tooltip` already present.) Each is a single-file component generated by shadcn.

---

## 5. Routing & placeholder pages

### Router setup

`BrowserRouter` with `basename="/app"`. Single root layout (`<AppLayout/>`) renders the sidebar + top bar with `<Outlet/>` in the content area. All routes nested under it.

### Route table

| Path (under `/app`) | Page component | In sidebar? |
|---|---|---|
| `/` | Dashboard | No (reached via brand mark) |
| `/movements` | Movements | Activity |
| `/transactions` | Transactions | Activity |
| `/transfers` | Transfers | Activity |
| `/review` | Review | Activity |
| `/accounts` | Accounts | Money |
| `/categories` | Categories | Money |
| `/budgets` | Budgets | Money |
| `/recurring` | Recurring Transactions | Tools |
| `/import` | Import | Tools |
| `/reports` | Reports | Tools |
| `/settings` | Settings | Bottom-pinned |
| `/support` | Support | Bottom-pinned |
| `/profile` | Profile | Avatar dropdown |
| `/security` | Security | Avatar dropdown |
| `*` | NotFound | — |

### `<PagePlaceholder>` component

A single reusable component:

```tsx
<PagePlaceholder
  title="Dashboard"
  description="The Dashboard page will land in a follow-up plan."
/>
```

Renders an `<h1>` (with `tabIndex={-1}`, focused on mount for screen readers) and a muted description inside a `Card`. Every placeholder page is a one-liner using it. Eliminates 16 nearly-identical files.

### Server-side wiring

- **New file:** `ProjectCeres/Controllers/AppController.cs` with one action `Index()` returning `View()`.
- **New file:** `ProjectCeres/Views/App/Index.cshtml` — minimal HTML host with `<div id="root">`, the localStorage pre-hydration script, and a `<vite-asset>`-style include.
- **New file:** `ProjectCeres/Helpers/ViteManifestHelper.cs` — reads `wwwroot/dist/.vite/manifest.json` (Vite emits this when `manifest: true` is set) and returns hashed asset paths for a given entry name. Reused by future entry points.
- **`Program.cs` change:** add `app.MapControllerRoute("app", "app/{*path}", new { controller = "App", action = "Index" })`. The `{*path}` catch-all is the key piece — every URL under `/app/` lands on the same view, then React Router takes over.
- **`vite.config.ts` change:** add `app: path.resolve(__dirname, 'app.html')` to `rollupOptions.input` (third entry alongside `main` + `designSystem`) and enable `build.manifest: true`.

### Dev server behaviour

In development (`pnpm dev` running on port 5173), `app.html` is served by Vite. The Razor view in dev mode serves a `<script type="module" src="http://localhost:5173/src/app/main.tsx">` tag pointing at the dev server, so HMR works while you load the page through ASP.NET Core. Production picks up the hashed bundle via the manifest helper. This is the same dev/prod split the existing Razor islands use.

### Deep linking

Visiting `https://localhost:7001/app/transactions` directly (hard refresh, bookmark, browser back) hits the catch-all route, returns the SPA host view, React Router reads the URL and renders the Transactions placeholder.

---

## 6. Responsive behaviour

### Three breakpoints

| Breakpoint | Range | Sidebar | Top bar |
|---|---|---|---|
| Mobile | `< 640px` (`< sm`) | Hidden; reachable via hamburger sheet | Hamburger left, wordmark center, search/avatar right; quick-add + notifications hidden |
| Tablet | `640px – 1023px` (`sm` – `lg`) | Rail by default (icon-only); toggle to expand | Standard layout |
| Desktop | `≥ 1024px` (`lg`+) | Expanded by default; toggle to rail | Standard layout |

### Defaults vs. user choice

The breakpoint determines the *default*; the user's `localStorage` choice *overrides* it on tablet and desktop. Mobile *always* hides the sidebar regardless of stored preference. Defaults apply on first visit (no localStorage value) or on resize across breakpoints.

### Resize behaviour

A `useMediaQuery` hook watches `(min-width: 640px)` and `(min-width: 1024px)`:
- Mobile → Tablet: sheet closes if open; sidebar materialises in rail mode (or user's preference if set).
- Tablet → Desktop: no forced change; sidebar keeps current state (rail or expanded per user choice).
- Any → Mobile: sidebar disappears; if a sheet was open it stays open.

### Content area

- Forms inside placeholder pages stack to single column on mobile (rule applies starting with the next plan; this plan has no forms).
- Tables get the horizontal-scroll wrapper rule from the spec when they arrive.
- The placeholder Card uses `max-w-2xl mx-auto p-6` so it reads at every width without media queries.

---

## 7. Accessibility baseline

The full WCAG 2.1 AA audit lives in `planning-phase3.md` §8 and is not done in this plan. The shell touches enough complex widgets (drawer, dropdown, modal, focus management on route change) that getting the basics right now is far cheaper than retrofitting later.

### Must-haves in this plan

- **Skip link.** First focusable element: an off-screen "Skip to main content" link (`sr-only`, revealed on `:focus`) that jumps focus to the `<main>` content area.
- **Landmark roles.** `<header>` for the top bar, `<nav aria-label="Primary">` for the sidebar nav, `<main>` for the content area, `<aside aria-label="Sidebar">` wrapping the sidebar.
- **Sidebar collapse toggle.** `aria-expanded={!collapsed}` and `aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}`.
- **Mobile drawer.** shadcn `Sheet` provides focus trap, Escape-to-close, `aria-modal` — verify during implementation, don't assume.
- **Rail-mode tooltips.** Each rail nav item: icon button has `aria-label={item.label}` (not just an SVG with no name) AND a shadcn `Tooltip` provides the visible label. Keyboard users get the same label that mouse users do.
- **Active route announcement.** Active `NavLink` gets `aria-current="page"`.
- **Focus management on route change.** On nav, focus moves to the page's `<h1>` (`tabIndex={-1}` + `useEffect` calling `.focus()`). Without this, screen-reader users hear nothing change after clicking nav.
- **Search modal.** `Dialog` from shadcn handles focus trap + restore. Internal `Input` gets `autoFocus`.
- **Avatar dropdown.** `DropdownMenu` is keyboard-navigable out of the box (arrow keys, Enter, Escape) — verify, don't assume.
- **Color is not the only differentiator.** Active nav-link uses both background color AND a 2px left border.
- **`<html lang="en">`** in `app.html` (will become dynamic when localization lands).

### Deferred to the full audit (planning-phase3.md §8)

- `eslint-plugin-jsx-a11y` integration
- `vitest-axe` per-component assertions
- Combobox audit (no combobox in this plan)
- Date picker audit (no date picker)
- Manual NVDA/VoiceOver walkthrough

---

## 8. Testing strategy

### What's tested

- `src/app/lib/use-keyboard-shortcut.test.ts` — `mod+k` fires on `meta+k` on Mac and `ctrl+k` otherwise; listener cleans up on unmount; doesn't fire when an `<input>` or `<textarea>` has focus.
- `src/app/lib/sidebar-storage.test.ts` — `readCollapsed()` and `writeCollapsed()` round-trip through `localStorage`; `readCollapsed()` returns `false` when storage is empty, malformed, or throws (private mode); `writeCollapsed()` is a no-op when storage throws.
- `src/app/layout/Sidebar.test.tsx` — renders all nav items in 3 groups + 2 bottom-pinned; active state applies to the matching route; rail mode renders icon-only buttons with `aria-label` set; toggle button updates `aria-expanded`.
- `src/app/layout/MobileDrawer.test.tsx` — clicking a nav item inside the drawer calls the close handler; drawer renders the same nav items as the desktop sidebar.
- `src/app/layout/AvatarMenu.test.tsx` — dropdown opens on trigger click; renders Profile, Security, Logout in that order; Logout opens the placeholder Dialog rather than navigating.
- `src/app/components/PagePlaceholder.test.tsx` — renders the title as `<h1>`, renders the description, focuses the heading on mount.
- `src/app/App.test.tsx` — smoke test wrapping `<App/>` in a `MemoryRouter`, asserting every route in the table renders its expected page (one assertion per route — catches regressions when routes are added/removed/renamed).

### Intentionally not tested

- Pixel-perfect sidebar widths (jsdom can't measure)
- Sheet open/close animation (shadcn already tests Sheet; we trust the primitive)
- Top-bar responsive layout (browser viewport thing, not jsdom)
- The pre-hydration localStorage script in `Index.cshtml` (a string of JS in a Razor view; smoke-verified manually)

### Manual verification

- `pnpm dev` + `dotnet run`, visit `https://localhost:7001/app` — sidebar + top bar render
- Toggle collapse, reload, confirm state persisted
- Cmd+K opens search modal, Escape closes
- Resize through 320px / 800px / 1400px — three layouts hit
- Hard refresh on `/app/transactions` deep link — Transactions placeholder renders (proves catch-all route works)
- Existing Razor app still works at `/Dashboard`, `/Movements`, etc. (proves we didn't break anything)

---

## 9. Out of scope

These belong to follow-up plans and are explicitly *not* in the App Shell plan:

- Real `/api/search` endpoint and search results UI
- Quick-add modal form + submit pipeline
- Notifications system (data model, API, polling/websocket)
- Authentication (login, registration, TOTP, sessions, logout)
- Profile page content
- Security page content (sessions list, IP blocking)
- Logout action wiring
- Dashboard page content
- Any non-placeholder page content under `/app/*`
- Removing the `/app/` URL prefix (final cleanup plan, after every feature is migrated)
- Dark mode toggle (token structure already exists; adding the toggle is a small follow-up)
- Localization runtime + language switcher (lives in its own plan; the `<html lang>` attribute is statically `en` for now)
- WCAG 2.1 AA full audit (accessibility baseline is in §7; full audit is a separate effort)
- `eslint-plugin-jsx-a11y` and `vitest-axe` tooling integration
