# Spec: SPA Migration + UX/UI Overhaul

> **Date:** 2026-04-28
> **Phase:** 3
> **Status:** Approved — ready for implementation planning

---

## Index

1. [Navigation Shell](#1-navigation-shell)
2. [Global Search](#2-global-search)
3. [Per-Table Search & Saved Searches](#3-per-table-search--saved-searches)
4. [Form Fields](#4-form-fields)
5. [Movements & Quick-Add](#5-movements--quick-add)
6. [SPA Migration Strategy](#6-spa-migration-strategy)

---

## 1. Navigation Shell

### Left Sidebar (primary navigation)

- Fixed position, full height, sits behind the top bar
- **Expanded state:** icon + label, ~240px wide
- **Collapsed state:** icon-only rail, ~56px wide
- Toggle button at the bottom of the sidebar
- Logo/wordmark at the top in both states; collapses to icon-only in rail mode

**Nav groups:**

| Group | Items |
|---|---|
| Main | Dashboard, Movements, Transactions, Transfers |
| Money | Accounts, Categories, Budgets |
| Tools | Recurring Transactions, Import, Reports |
| (bottom-pinned) | Settings, Support |

All nav items use Lucide icons. Every item must have an icon that works standalone in rail mode.

### Top Bar (global)

- Fixed position, full width, sits above the sidebar
- Left: logo area — width matches sidebar; shifts when sidebar collapses
- Center: global search input (see Section 2)
- Right: quick-add "+" button, notifications bell, user avatar
- User avatar opens a dropdown: Profile, Preferences, Security (sessions), Logout

### Responsiveness (applies to all sections)

- **Desktop (≥1024px):** sidebar expanded by default, collapsible to rail
- **Tablet (640px–1023px):** sidebar collapsed to icon-rail by default
- **Mobile (<640px):** sidebar hidden entirely; hamburger button in top bar toggles a slide-over drawer
- Top bar global search collapses to a search icon on mobile; tapping expands it inline
- Tables scroll horizontally on small screens; priority columns pinned left
- Forms stack to single-column on mobile

---

## 2. Global Search

### MVP behavior

- Triggered from the top bar search input or keyboard shortcut (`⌘K` / `Ctrl+K`)
- Opens a modal/popover with a focused search input and grouped results below
- Searches across: transactions (description, amount), accounts (name), categories, recurring transactions, reports
- Results grouped by entity type with a label header (e.g. "Transactions · 4 results")
- Clicking a result navigates directly to that entity's detail or edit page
- Keyboard navigable: arrow keys to move between results, Enter to navigate, Escape to close
- No filters in MVP — plain text search, grouped results only

### Future evolution (not in this spec)

- Filter layer: narrow results by entity type, date range, account, category
- Evolves toward a full command palette (navigate to pages, trigger actions, not just find records)

---

## 3. Per-Table Search & Saved Searches

### Filter bar

- Appears above every table (Transactions, Transfers, Movements, Accounts, Categories, Recurring, Reports)
- Contains: text search input + entity-specific filter chips
- Entity-specific filters:
  - **Transactions / Movements:** date range, category, account, type (income/expense/transfer), cleared status
  - **Transfers:** date range, source account, destination account
  - **Accounts:** type (asset/liability), currency, active/inactive
  - **Categories:** type (income/expense), active/inactive
  - **Recurring Transactions:** frequency, next due date range, active/inactive
- Active filters shown as dismissible chips — each chip has an ✕ to remove it individually
- "Clear all" link resets all filters at once

### Saved searches

- "Save search" button appears when any filter is active
- Opens a small popover: name input + Save button
- Saved searches accessible from a dropdown next to the filter bar
- Selecting a saved search applies all its filters instantly
- Delete a saved search from the same dropdown (with confirmation)
- Stored server-side per user per table — persist across devices and sessions
- MVP: filters + text search saved. Future (Phase 4+): include column visibility and sort order (named views)

---

## 4. Form Fields

### Visual polish

- **Floating labels:** label starts inside the field, animates above on focus or when a value is present
- **Focus ring:** brand color, smooth transition — not the browser default
- **Error state:** red border + inline message below the field; never a top-of-form summary
- **Required indicator:** `*` next to the label, with a legend at the bottom of the form ("* Required")
- **Disabled state:** muted appearance, consistent across all input types
- **Submit button:** spinner + disabled during loading; re-enables on error so the user can retry

### Smarter field behavior

- **Category / account selectors:** searchable combobox — type to filter, keyboard navigable, shows recently used items at top
- **Date fields:** date picker with presets: Today, Yesterday, This week, This month, Last month, Custom range
- **Amount fields:** formats as currency as the user types; respects the user's currency setting from preferences
- **Multi-step forms** (onboarding, import): progress indicator at the top showing step N of M
- **Unsaved changes:** browser `beforeunload` warning + in-app confirmation dialog when navigating away with unsaved changes

### Cancel / back navigation

- All forms track the originating route (via React Router state or a `returnTo` param) and "Cancel" returns there
- Never a hardcoded redirect route — Cancel always goes back to where the user came from
- Full audit of all cancel/back paths during implementation; recurring transaction dismiss/cancel is a known case

---

## 5. Movements & Quick-Add

### Movements page fixes

- Add "New Transaction" and "New Transfer" buttons to the Movements page header
- After creating from Movements, return to Movements — not to the Transactions or Transfers index
- Movements is a fully capable entry point, not a read-only view

### Quick-add from anywhere

- A "+" button in the top bar (right side, next to notifications) always visible
- Opens a compact modal with enough fields to record a transaction or transfer without leaving the current page: type selector (Transaction / Transfer), date, amount, account, category (if transaction), description
- On save: shows a toast confirmation, closes the modal, stays on the current page
- Does not navigate away — designed for rapid entry during review sessions

---

## 6. SPA Migration Strategy

### Approach: Design-first, migrate feature by feature

Define the full design system and shell before porting any page. Each ported page gets the final design on arrival — no second-pass redesign.

### Phase order

1. **Design system foundation** — tokens (colors, typography, spacing, radius, shadows, motion), CSS variables, shadcn/ui overrides, `docs/design-system.md`. Zero visible UI change; everything downstream depends on it.
2. **App shell** — sidebar (expanded / icon-rail / mobile drawer), top bar (logo, global search, quick-add, notifications, avatar dropdown), responsive behavior across all breakpoints.
3. **Auth screens** — login, TOTP verification, registration, password reset. Outside the app shell (centered card layout). Built fresh — no Razor equivalent.
4. **Onboarding wizard** — first-run flow (create first account, record opening balance, see net worth). Outside the app shell.
5. **Dashboard** — first page inside the shell. `DashboardApiController` already exists; API work is minimal.
6. **Movements + Transactions + Transfers** — highest daily usage. Includes quick-add modal, per-table search, and saved searches.
7. **Accounts + Categories + Budgets + Recurring Transactions** — management screens.
8. **Reports + Import/Export** — complex, lower frequency.
9. **Settings + Sessions + Support** — lowest frequency; includes saved searches management UI.

### Migration mechanics (per feature area)

1. Build API endpoints (controllers → DTOs) and verify with integration tests — Razor still running
2. Build React page against the live API — Razor still running as fallback
3. Verify end-to-end in React, then delete Razor views and MVC actions for that area
4. Repeat — never a big-bang deletion of all Razor at once
5. Remove MVC infrastructure from `Program.cs` last, once no Razor views remain

### Hosting model

- **Option A (selected):** React build output copied to ASP.NET Core `wwwroot/`; served as static files with `app.MapFallbackToFile("index.html")` for React Router. One deployable unit, no separate web server. Simplest for local-first beta.
- Revisit at Phase 4 if a mobile client is added (would warrant Option B: separate origins with nginx/Caddy).

### Open decisions (resolve at implementation kickoff)

- Authentication framework choice (ASP.NET Core Identity vs. custom) — gates auth screens
- Hosting platform — gates production deployment; local testing is not blocked
- Email service — gates auth emails, digest emails, session alerts
- CI service — gates dependency scanning pipeline
