# Stage 13 — Cookie Consent Banner + Legal Pages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the EU-beta legal-gate UI — `/privacy` + `/legal` public pages (EN+ES), a granular AEPD cookie-consent banner, and a reachable consent-revocation affordance — as a client-only change.

**Architecture:** React SPA (`ProjectCeres.Client/`), client-only. Two new public routes added to `App.tsx`'s existing public branch, wrapped in a new minimal public layout (`LegalLayout`). A consent banner mounted at `App` root (sibling of `<Toaster>`) so it shows pre-auth and authed. A `consent-cookie.ts` utility mirroring `lang-cookie.ts`. A `<Footer>` on public layouts only. A "Privacy & cookies" section added to the authed Settings page as the revocation re-entry. Legal text is a **codebase-grounded draft with `[CONFIRM WITH COUNSEL]` markers** (verified facts stated; unverifiable business/legal facts marked), stored in per-language content files — not live until a human reviews it. No backend, no entity, no migration.

**Tech Stack:** React 19 + Vite + TypeScript, react-router-dom (relative routes under the SPA root), react-i18next, Tailwind CSS v4 (`@theme inline` / `@layer`), base-ui shadcn primitives, Vitest + React Testing Library, Playwright E2E.

**Spec:** `docs/superpowers/specs/2026-10-04-stage-13-cookie-consent-legal-pages-design.md`

## Global Constraints

- **Client-only.** No change under `ProjectCeres/` (server), no entity, no EF migration, no RLS. The IUserOwned / registry / verify-stage-completeness gates do NOT apply (verified: no server-side consent code exists).
- **Frontend-orchestrator owns the build.** Each UI task routes through it (CLAUDE.md § Frontend Work); no ad-hoc `frontend-design`/`impeccable`.
- **Design-system-first.** The prose recipe lands in `ProjectCeres.Client/src/index.css` + is documented in `docs/design-system.md` BEFORE any page consumes it. No hex literals, no ad-hoc spacing — use existing tokens.
- **base-ui primitives, not Radix.** `render={...}` not `asChild`; `onClick` not `onSelect`; `onValueChange={(v, eventDetails) => …}`. Reuse existing shapes in `src/components/ui/`.
- **EN/ES parity is a ship gate.** Every new short-copy key exists in BOTH `en.json` and `es.json`; the files stay at equal line count (currently 342/342). Both policy content files (EN + ES) present.
- **Routes are relative, no leading slash** in route *definitions* (`path="privacy"`), matching the existing public branch; `<Link>`/`navigate` targets use a leading slash (`to="/privacy"`), matching existing usage.
- **No trailing ellipsis** in any button, menu item, or AlertDialog action label (`feedback_no_trailing_ellipsis_in_labels`).
- **Codebase-grounded legal draft, NOT final text.** Each content file opens with a `[DRAFT — NOT legally reviewed; do not publish until counsel reviews]` banner. State codebase-verified facts truthfully (cookies, data categories, retention windows, the 6-year financial-retention-overrides-erasure point, the real rights-exercise path); wrap every business-fact (legal name/NIF/address, subprocessor names, contact) and legal judgment (per-purpose legal basis) as an inline `[CONFIRM WITH COUNSEL: …]` marker. NEVER fill a marker with a guess. Full rules: spec § "Grounded-draft content rules". `docs/legal.md` is read-only.
- **24-month consent cookie**, first-party, `SameSite=Lax; Secure; Path=/`, NOT HttpOnly, no identifier (AEPD re-consent window).
- **Show-then-approval + UX checklist.** Show the rendered result; wait for explicit approval before the final commit. Run the UX checklist (golden / empty / error / 375px / all-nav); prefer Playwright E2E as verification over a manual handoff.
- **Toolchain:** `pnpm --dir ProjectCeres.Client …`, never `cd`-then-`pnpm`, never `npm`. `pnpm build` and `pnpm test` must exit 0 before the stage is reported done.

## File Structure

New files:
- `ProjectCeres.Client/src/app/i18n/consent-cookie.ts` — consent cookie read/write/clear (sibling of `lang-cookie.ts`).
- `ProjectCeres.Client/src/app/i18n/consent-cookie.test.ts` — unit tests for the cookie utility.
- `ProjectCeres.Client/src/app/layout/LegalLayout.tsx` — minimal public layout (brand header + scrollable prose + Footer).
- `ProjectCeres.Client/src/app/layout/Footer.tsx` — shared footer (legal links + manage-cookies + language toggle).
- `ProjectCeres.Client/src/app/layout/Footer.test.tsx`
- `ProjectCeres.Client/src/app/components/ConsentBanner.tsx` — granular AEPD banner.
- `ProjectCeres.Client/src/app/components/ConsentBanner.test.tsx`
- `ProjectCeres.Client/src/app/pages/legal/Privacy.tsx` — privacy page (renders privacy content file).
- `ProjectCeres.Client/src/app/pages/legal/Legal.tsx` — Aviso Legal + Cookie Policy anchored sections.
- `ProjectCeres.Client/src/app/pages/legal/legal-pages.test.tsx` — render + anchor tests.
- `ProjectCeres.Client/src/app/pages/legal/content/privacy.en.tsx` + `privacy.es.tsx` — codebase-grounded privacy draft (counsel markers).
- `ProjectCeres.Client/src/app/pages/legal/content/legal.en.tsx` + `legal.es.tsx` — codebase-grounded aviso-legal + cookie-policy draft (counsel markers).
- `ProjectCeres.Client/e2e/consent-legal.spec.ts` — Playwright E2E (path matches existing e2e dir convention; confirm at Task 9).

Modified files:
- `ProjectCeres.Client/src/index.css` — add the prose container recipe.
- `docs/design-system.md` — document the prose recipe.
- `ProjectCeres.Client/src/app/App.tsx` — add the two public routes under a `LegalLayout` route element; mount `<ConsentBanner>` at root.
- `ProjectCeres.Client/src/app/layout/AuthLayout.tsx` — add `<Footer>` (replacing the bare toggle footer, or alongside — Task 5 decides).
- `ProjectCeres.Client/src/app/features/settings/SettingsPage.tsx` — add the "Privacy & cookies" section.
- `ProjectCeres.Client/src/app/i18n/locales/en.json` + `es.json` — new short-copy keys (banner, footer, settings section).

Prose content is rendered as **TSX components** (not Markdown + `dangerouslySetInnerHTML`) — the Stage 14 CSP bans `dangerouslySetInnerHTML` via eslint `no-restricted-syntax`, and TSX keeps counsel editing in a readable, typed file without a Markdown renderer dependency. This resolves the spec's open "content-file format" question in favor of TSX.

---

### Task 1: Consent-cookie utility

**Files:**
- Create: `ProjectCeres.Client/src/app/i18n/consent-cookie.ts`
- Test: `ProjectCeres.Client/src/app/i18n/consent-cookie.test.ts`

**Interfaces:**
- Consumes: nothing (mirrors `lang-cookie.ts` DOM-cookie pattern).
- Produces:
  - `const CONSENT_COOKIE_NAME = 'cookie_consent'`
  - `type ConsentCategories = { necessary: true; analytics: boolean; preferences: boolean }`
  - `type ConsentRecord = { categories: ConsentCategories; timestamp: string }` (timestamp = ISO-8601)
  - `function readConsent(): ConsentRecord | null` — null if absent or malformed
  - `function writeConsent(categories: ConsentCategories): void` — stamps `timestamp`, serializes JSON, 24-month Max-Age
  - `function clearConsent(): void` — expires the cookie (Max-Age=0), so the banner re-shows

- [ ] **Step 1: Write the failing test**

```ts
import { describe, it, expect, beforeEach } from 'vitest';
import { CONSENT_COOKIE_NAME, readConsent, writeConsent, clearConsent } from './consent-cookie';

function clearDocCookie() {
  document.cookie = `${CONSENT_COOKIE_NAME}=; Path=/; Max-Age=0`;
}

describe('consent-cookie', () => {
  beforeEach(clearDocCookie);

  it('returns null when no consent cookie is set', () => {
    expect(readConsent()).toBeNull();
  });

  it('round-trips a written consent record', () => {
    writeConsent({ necessary: true, analytics: true, preferences: false });
    const rec = readConsent();
    expect(rec).not.toBeNull();
    expect(rec!.categories).toEqual({ necessary: true, analytics: true, preferences: false });
    expect(typeof rec!.timestamp).toBe('string');
    expect(Number.isNaN(Date.parse(rec!.timestamp))).toBe(false);
  });

  it('always forces necessary=true even if a caller passes false', () => {
    // necessary cookies are not consent-gated; the type pins it true, this guards runtime.
    writeConsent({ necessary: true, analytics: false, preferences: false });
    expect(readConsent()!.categories.necessary).toBe(true);
  });

  it('returns null for a malformed cookie value', () => {
    document.cookie = `${CONSENT_COOKIE_NAME}=not-json; Path=/`;
    expect(readConsent()).toBeNull();
  });

  it('clearConsent removes the record so readConsent is null again', () => {
    writeConsent({ necessary: true, analytics: true, preferences: true });
    expect(readConsent()).not.toBeNull();
    clearConsent();
    expect(readConsent()).toBeNull();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `pnpm --dir ProjectCeres.Client exec vitest run src/app/i18n/consent-cookie.test.ts`
Expected: FAIL — module `./consent-cookie` not found.

- [ ] **Step 3: Write minimal implementation**

```ts
export const CONSENT_COOKIE_NAME = 'cookie_consent';

export type ConsentCategories = {
  necessary: true;
  analytics: boolean;
  preferences: boolean;
};

export type ConsentRecord = {
  categories: ConsentCategories;
  timestamp: string;
};

/** Read + parse the consent cookie. Returns null if absent or malformed. */
export function readConsent(): ConsentRecord | null {
  if (typeof document === 'undefined') return null;
  const match = document.cookie.match(
    new RegExp(`(?:^|;\\s*)${CONSENT_COOKIE_NAME}=([^;]+)`),
  );
  if (!match) return null;
  try {
    const parsed = JSON.parse(decodeURIComponent(match[1])) as ConsentRecord;
    if (!parsed || typeof parsed !== 'object' || !parsed.categories) return null;
    return parsed;
  } catch {
    return null;
  }
}

/** Write the consent record. 24-month Max-Age (AEPD re-consent), first-party,
 *  SameSite=Lax, Secure, NOT HttpOnly (the SPA reads it), no identifier. */
export function writeConsent(categories: ConsentCategories): void {
  if (typeof document === 'undefined') return;
  const twentyFourMonthsSeconds = 60 * 60 * 24 * 365 * 2;
  const record: ConsentRecord = {
    categories: { ...categories, necessary: true },
    timestamp: new Date().toISOString(),
  };
  const value = encodeURIComponent(JSON.stringify(record));
  document.cookie = `${CONSENT_COOKIE_NAME}=${value}; Path=/; Max-Age=${twentyFourMonthsSeconds}; SameSite=Lax; Secure`;
}

/** Expire the cookie so the banner re-shows (GDPR Art. 7(3) withdrawal). */
export function clearConsent(): void {
  if (typeof document === 'undefined') return;
  document.cookie = `${CONSENT_COOKIE_NAME}=; Path=/; Max-Age=0; SameSite=Lax; Secure`;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `pnpm --dir ProjectCeres.Client exec vitest run src/app/i18n/consent-cookie.test.ts`
Expected: PASS (5/5).

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/i18n/consent-cookie.ts ProjectCeres.Client/src/app/i18n/consent-cookie.test.ts
git commit -m "feat(13): consent-cookie utility (read/write/clear, 24-month, no PII)"
```

---

### Task 2: Prose design-system recipe

**Files:**
- Modify: `ProjectCeres.Client/src/index.css` (add under `@layer components`, after the existing component recipes)
- Modify: `docs/design-system.md` (document under a new "Long-form prose" entry in the recipes section)

**Interfaces:**
- Produces: a `.prose-legal` class (or `@utility prose-legal` per the file's Tailwind v4 convention — confirm which the file uses in Step 1) that sets max-width, line-height, heading rhythm, and link styling using existing tokens only. Consumed by `LegalLayout` (Task 4) and the content components (Task 7).

- [ ] **Step 1: Read `index.css` to confirm the recipe idiom**

Read `ProjectCeres.Client/src/index.css` fully. Confirm whether component recipes are written as `@layer components { .name { … } }` or Tailwind v4 `@utility name { … }`. Match the file's existing idiom exactly. Confirm the token names in scope (`--font-sans`, spacing, `--color-*`, `--radius`). No new raw values — the recipe composes existing tokens.

- [ ] **Step 2: Write the recipe (no test — CSS; verification is visual + `pnpm build`)**

Add a `prose-legal` recipe: constrain measure (`max-width: 65ch`), comfortable `line-height`, vertical rhythm between headings and paragraphs, muted color for secondary text, link color from the existing accent token, and list spacing. Use only tokens already defined in the file. Example shape (adapt class/utility form to the file's idiom):

```css
/* Long-form legal/policy prose. The app has no other long-form surface;
   constrained measure + rhythm for readability on /privacy and /legal. */
.prose-legal {
  max-width: 65ch;
  line-height: 1.7;
}
.prose-legal h2 { font-size: var(--text-xl); font-weight: 600; margin-top: 2rem; margin-bottom: 0.75rem; }
.prose-legal h3 { font-size: var(--text-lg); font-weight: 600; margin-top: 1.5rem; margin-bottom: 0.5rem; }
.prose-legal p { margin-bottom: 1rem; }
.prose-legal ul { margin-bottom: 1rem; padding-left: 1.5rem; list-style: disc; }
.prose-legal li { margin-bottom: 0.375rem; }
.prose-legal a { color: var(--color-primary); text-decoration: underline; }
```

- [ ] **Step 3: Document in `docs/design-system.md`**

Add a "Long-form prose (`prose-legal`)" subsection under the recipes section: what it is for (the only long-form surface — legal/policy pages), the measure/rhythm decisions, and that it composes existing tokens. Follow the doc's existing recipe-entry format.

- [ ] **Step 4: Verify the build consumes the recipe cleanly**

Run: `pnpm --dir ProjectCeres.Client build`
Expected: exit 0, no Tailwind warning about an unknown utility/class.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/index.css docs/design-system.md
git commit -m "feat(13): prose-legal design-system recipe for long-form pages"
```

---

### Task 3: Codebase-grounded legal content components

**Files:**
- Create: `ProjectCeres.Client/src/app/pages/legal/content/privacy.en.tsx`
- Create: `ProjectCeres.Client/src/app/pages/legal/content/privacy.es.tsx`
- Create: `ProjectCeres.Client/src/app/pages/legal/content/legal.en.tsx`
- Create: `ProjectCeres.Client/src/app/pages/legal/content/legal.es.tsx`

**Interfaces:**
- Produces, from each file, a default-exported React component rendering semantic JSX (`<h2>`, `<p>`, `<ul>`), NOT a string and NOT `dangerouslySetInnerHTML`:
  - `privacy.en.tsx` / `privacy.es.tsx` → `PrivacyContentEn` / `PrivacyContentEs`
  - `legal.en.tsx` / `legal.es.tsx` → `LegalContentEn` / `LegalContentEs`, each rendering two `<section>`s with `id="aviso-legal"` and `id="cookies"` so the anchors resolve.

**This is a codebase-grounded DRAFT, not a bare placeholder.** Follow the spec's
"Grounded-draft content rules" section exactly. The rule: state every fact I can
verify from the code / `legal.md` truthfully; wrap every business-fact or
legal-judgment I cannot verify as an inline `[CONFIRM WITH COUNSEL: …]` marker.
NEVER fill a `[CONFIRM …]` with a guess. `docs/legal.md` is read-only — read facts
from it, do not edit it.

- [ ] **Step 1: Write the four content components**

**Every file opens with a banner paragraph:**
`<p><strong>[DRAFT — generated from the codebase, NOT legally reviewed. Do not publish as the live policy until counsel has reviewed it.]</strong></p>`

`privacy.*` headings + grounded content (state as FACT — these are verified):
- **What data we collect** — account email; IP address + user-agent (security logs: `FailedLoginAttempt`, `UserSession`, `AuditLog`, `UserBlockedIp`); uploaded attachment file names + financial attachment files; your financial data (accounts, transactions, budgets, categories).
- **Why we process it (legal basis)** — propose per purpose, each wrapped: `[CONFIRM WITH COUNSEL: legal basis — service delivery = contract]`, `[CONFIRM WITH COUNSEL: legal basis — financial retention = legal obligation]`, `[CONFIRM WITH COUNSEL: legal basis — security logs = legitimate interest]`.
- **How long we keep it** — state as fact: soft-deleted saved reports 90 days; audit logs 12 months; failed-login logs 1 year; account closure 30-day grace → 150-day sealed archive → permanent deletion at day 180. **AND** the critical correctness point: financial/accounting records retained 6 years (Código de Comercio Art. 30) / tax 4–6 years, which **overrides the right to erasure** (GDPR Art. 17(3)(b)) — on erasure, identifiers are anonymised but anonymised financial records are retained for the legal period.
- **Who we share it with** — `[CONFIRM: hosting provider — name + DPA signed]`, `[CONFIRM: email service — name + DPA signed]`. (`legal.md` names these as TBD; do not guess vendor names.) State plainly: no third-party behavioural analytics in Phase 3.
- **Your rights** — access, rectification, erasure, portability, objection. Describe the real user path: data export via Settings → Account → "download a copy"; erasure via Settings → Account, with a 72-hour emailed cancel link. (Describe the path, not the endpoint names.)
- **How to contact us / exercise rights** — `[CONFIRM WITH COUNSEL: contact email + DPO/supervisory-authority details]`.
- Effective date: `[CONFIRM: effective date]`.

`legal.*` — two `<section>`s:
- `<section id="aviso-legal">` — Aviso Legal (LSSI-CE Art. 10): `[CONFIRM: legal entity name]`, `[CONFIRM: NIF]`, `[CONFIRM: registered address]`, `[CONFIRM: contact email]`. These are business-identity facts — ALL markers, never invented.
- `<section id="cookies">` — Cookie Policy: state as fact the cookies actually set — `__Host-Session` (session), `__Host-Persist` (remember-me), `__Host-XSRF` (CSRF), `lang` (language), `cookie_consent` (this banner's own record) — each strictly-necessary, none consent-gated today; how to withdraw (the banner's Manage preferences / Settings re-entry).

EN and ES files carry the SAME structure, section ids, and the SAME set of
`[CONFIRM …]` markers (translate the prose; keep the markers identical). Parity.

- [ ] **Step 2: Verify compile + marker parity** — static content; rendering + anchors asserted in Task 7. Verify:

Run: `pnpm --dir ProjectCeres.Client exec tsc --noEmit` (expect exit 0).
Then confirm EN and ES carry the same `[CONFIRM` marker count:
`grep -c "\[CONFIRM" ProjectCeres.Client/src/app/pages/legal/content/privacy.en.tsx ProjectCeres.Client/src/app/pages/legal/content/privacy.es.tsx` (counts must match; same for legal.en/es).

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/app/pages/legal/content/
git commit -m "feat(13): codebase-grounded EN/ES privacy + legal draft with counsel markers"
```

---

### Task 4: LegalLayout (minimal public layout)

**Files:**
- Create: `ProjectCeres.Client/src/app/layout/LegalLayout.tsx`

**Interfaces:**
- Consumes: `<Outlet>` (react-router), `BrandMark` (`./BrandMark`), the `<Footer>` from Task 5.
- Produces: `export function LegalLayout()` — a full-height column: brand header (links to `/`), a scrollable main region wrapping `<Outlet>` in a `.prose-legal` container centered with page gutters, and `<Footer>` at the bottom. No app shell, no auth dependency.

> **Sequencing note:** Task 4 imports `<Footer>` from Task 5. Build Task 5 first OR stub the Footer import and wire it in Task 5. The subagent-driven executor should dispatch Task 5 before Task 4, or batch 4+5. Documented here so the executor orders them correctly.

- [ ] **Step 1: Write the layout**

```tsx
import { Link, Outlet } from 'react-router-dom';
import { BrandMark } from './BrandMark';
import { Footer } from './Footer';

/**
 * Minimal public layout for the legal/policy pages. Neither AuthLayout
 * (420px card) nor AppLayout (authed h-screen shell) fits long-form public
 * text, so these pages get their own column: brand header, scrollable prose,
 * shared footer. No auth dependency — reachable signed-out.
 */
export function LegalLayout() {
  return (
    <div className="min-h-dvh flex flex-col bg-background">
      <header className="border-b px-4 py-4 sm:px-6">
        <Link to="/" aria-label="Project Ceres home">
          <BrandMark />
        </Link>
      </header>
      <main className="flex-1 px-4 py-8 sm:px-6">
        <div className="prose-legal mx-auto">
          <Outlet />
        </div>
      </main>
      <Footer />
    </div>
  );
}
```

- [ ] **Step 2: Compile check** (the dedicated render test lives in Task 8, where the routes are wired and the layout can actually be mounted)

Run: `pnpm --dir ProjectCeres.Client exec tsc --noEmit`
Expected: exit 0 (requires Task 5's Footer to exist).

- [ ] **Step 3: Commit**

```bash
git add ProjectCeres.Client/src/app/layout/LegalLayout.tsx
git commit -m "feat(13): LegalLayout minimal public layout for legal pages"
```

---

### Task 5: Shared Footer

**Files:**
- Create: `ProjectCeres.Client/src/app/layout/Footer.tsx`
- Test: `ProjectCeres.Client/src/app/layout/Footer.test.tsx`

**Interfaces:**
- Consumes: `Link` (react-router), `LanguageToggle` (`../components/LanguageToggle`), `useTranslation` (react-i18next), a `onManageCookies` opener — see note below.
- Produces: `export function Footer()` — a `<footer>` with: legal links (`<Link to="/legal#aviso-legal">`, `<Link to="/privacy">`, `<Link to="/legal#cookies">`), a "Manage cookie preferences" button, and `<LanguageToggle>`. Copy via i18n keys (Task 6).

> **Manage-cookies wiring:** the banner (Task 8) is mounted at App root and owns its open/close state. The Footer's "Manage cookie preferences" button needs to signal that banner to open. Decide in Step 1 between: (a) a tiny module-level event (a `consent-events.ts` emitter the banner subscribes to), or (b) lifting banner open-state into a React context provided at App root. Prefer (a) — zero provider nesting, and the Footer renders in layouts outside any consent provider. The plan assumes (a): `consent-events.ts` exports `openConsentManager()` + `onOpenConsentManager(cb)`.

- [ ] **Step 1: Write `consent-events.ts` + the failing Footer test**

Create `ProjectCeres.Client/src/app/i18n/consent-events.ts` (or `src/app/lib/consent-events.ts` — place beside the banner's other consent modules; confirm the consent utility's dir in Step 1):

```ts
type Cb = () => void;
const listeners = new Set<Cb>();
export function openConsentManager() { listeners.forEach((cb) => cb()); }
export function onOpenConsentManager(cb: Cb) { listeners.add(cb); return () => listeners.delete(cb); }
```

Footer test (`Footer.test.tsx`), rendered inside `<MemoryRouter>` + i18n provider:

```tsx
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { Footer } from './Footer';
import * as events from '../i18n/consent-events';

function renderFooter() {
  return render(<MemoryRouter><Footer /></MemoryRouter>);
}

describe('Footer', () => {
  it('renders the three legal links with correct targets', () => {
    renderFooter();
    expect(screen.getByRole('link', { name: /privacy/i })).toHaveAttribute('href', '/privacy');
    expect(screen.getByRole('link', { name: /aviso legal/i })).toHaveAttribute('href', '/legal#aviso-legal');
    expect(screen.getByRole('link', { name: /cookie/i })).toHaveAttribute('href', '/legal#cookies');
  });

  it('manage-cookies button fires openConsentManager', async () => {
    const spy = vi.spyOn(events, 'openConsentManager');
    renderFooter();
    await userEvent.click(screen.getByRole('button', { name: /manage cookie preferences/i }));
    expect(spy).toHaveBeenCalledOnce();
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `pnpm --dir ProjectCeres.Client exec vitest run src/app/layout/Footer.test.tsx`
Expected: FAIL — `Footer` not found.

- [ ] **Step 3: Write the Footer**

```tsx
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { LanguageToggle } from '../components/LanguageToggle';
import { openConsentManager } from '../i18n/consent-events';

export function Footer() {
  const { t } = useTranslation();
  return (
    <footer className="border-t px-4 py-6 sm:px-6">
      <nav className="mx-auto flex max-w-[65ch] flex-wrap items-center gap-x-4 gap-y-2 text-sm text-muted-foreground">
        <Link to="/legal#aviso-legal" className="hover:text-foreground">{t('legal.footer.avisoLegal')}</Link>
        <Link to="/privacy" className="hover:text-foreground">{t('legal.footer.privacy')}</Link>
        <Link to="/legal#cookies" className="hover:text-foreground">{t('legal.footer.cookiePolicy')}</Link>
        <Button variant="link" className="h-auto p-0 text-sm text-muted-foreground hover:text-foreground" onClick={openConsentManager}>
          {t('legal.footer.manageCookies')}
        </Button>
        <span className="ml-auto"><LanguageToggle /></span>
      </nav>
    </footer>
  );
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `pnpm --dir ProjectCeres.Client exec vitest run src/app/layout/Footer.test.tsx`
Expected: PASS (2/2).

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/layout/Footer.tsx ProjectCeres.Client/src/app/layout/Footer.test.tsx ProjectCeres.Client/src/app/i18n/consent-events.ts
git commit -m "feat(13): shared Footer + consent-events opener"
```

---

### Task 6: i18n short-copy keys (EN + ES)

**Files:**
- Modify: `ProjectCeres.Client/src/app/i18n/locales/en.json`
- Modify: `ProjectCeres.Client/src/app/i18n/locales/es.json`

**Interfaces:**
- Produces a new top-level `legal` namespace in BOTH files with identical key structure:
  - `legal.footer.{avisoLegal,privacy,cookiePolicy,manageCookies}`
  - `legal.banner.{title,body,acceptAll,rejectAll,managePreferences,save}`
  - `legal.banner.categories.{necessary,necessaryDesc,analytics,analyticsDesc,preferences,preferencesDesc}`
  - `legal.settings.{title,description,managePreferences,viewPrivacy,viewLegal}`

- [ ] **Step 1: Add the `legal` namespace to `en.json`**

Insert a `"legal": { … }` block with the keys above, English copy. No trailing ellipsis in any label. "Manage cookie preferences", "Accept all", "Reject all", "Save preferences" — equal-weight button copy.

- [ ] **Step 2: Add the identical-structure `legal` block to `es.json`**

Spanish copy for the same keys. "Gestionar preferencias de cookies", "Aceptar todo", "Rechazar todo", "Guardar preferencias", "Aviso legal", "Privacidad", "Política de cookies".

- [ ] **Step 3: Verify parity**

Run:
```bash
pnpm --dir ProjectCeres.Client exec tsc --noEmit
wc -l ProjectCeres.Client/src/app/i18n/locales/en.json ProjectCeres.Client/src/app/i18n/locales/es.json
```
Expected: equal line counts; if the repo has an i18n-parity test (search `grep -rl "parity" ProjectCeres.Client/src`), run it and expect PASS.

- [ ] **Step 4: Commit**

```bash
git add ProjectCeres.Client/src/app/i18n/locales/en.json ProjectCeres.Client/src/app/i18n/locales/es.json
git commit -m "feat(13): EN/ES i18n keys for legal footer, banner, settings"
```

---

### Task 7: Privacy + Legal pages

**Files:**
- Create: `ProjectCeres.Client/src/app/pages/legal/Privacy.tsx`
- Create: `ProjectCeres.Client/src/app/pages/legal/Legal.tsx`
- Test: `ProjectCeres.Client/src/app/pages/legal/legal-pages.test.tsx`

**Interfaces:**
- Consumes: the content components (Task 3), current language from `i18n`/`readLangCookie`, `useDocumentTitle`.
- Produces: `export function Privacy()` and `export function Legal()` — each selects the EN or ES content component by current language and renders it. `Legal` renders content containing the `#aviso-legal` + `#cookies` sections.

- [ ] **Step 1: Write the failing page tests**

```tsx
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Privacy } from './Privacy';
import { Legal } from './Legal';

describe('legal pages', () => {
  it('Privacy renders the privacy heading structure', () => {
    render(<MemoryRouter><Privacy /></MemoryRouter>);
    expect(screen.getByRole('heading', { name: /what data we collect/i })).toBeInTheDocument();
  });

  it('Privacy states the retention windows (30-day / 180-day)', () => {
    render(<MemoryRouter><Privacy /></MemoryRouter>);
    expect(screen.getByText(/30[- ]day/i)).toBeInTheDocument();
    expect(screen.getByText(/180[- ]day/i)).toBeInTheDocument();
  });

  it('Privacy states the 6-year financial-record retention that limits erasure', () => {
    // The correctness point a generic template misses (Art. 17(3)(b) exemption).
    render(<MemoryRouter><Privacy /></MemoryRouter>);
    expect(screen.getByText(/6[- ]year/i)).toBeInTheDocument();
    expect(screen.getByText(/erasure/i)).toBeInTheDocument();
  });

  it('Legal renders both anchored sections', () => {
    const { container } = render(<MemoryRouter><Legal /></MemoryRouter>);
    expect(container.querySelector('#aviso-legal')).not.toBeNull();
    expect(container.querySelector('#cookies')).not.toBeNull();
  });

  it('legal content carries the not-legally-reviewed DRAFT banner', () => {
    render(<MemoryRouter><Legal /></MemoryRouter>);
    expect(screen.getByText(/not legally reviewed/i)).toBeInTheDocument();
  });

  it('Aviso Legal leaves business-identity facts as CONFIRM markers (never guessed)', () => {
    render(<MemoryRouter><Legal /></MemoryRouter>);
    expect(screen.getByText(/\[CONFIRM/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `pnpm --dir ProjectCeres.Client exec vitest run src/app/pages/legal/legal-pages.test.tsx`
Expected: FAIL — `Privacy`/`Legal` not found.

- [ ] **Step 3: Write the pages**

```tsx
// Privacy.tsx
import { useTranslation } from 'react-i18next';
import { useDocumentTitle } from '../../lib/use-document-title';
import PrivacyContentEn from './content/privacy.en';
import PrivacyContentEs from './content/privacy.es';

export function Privacy() {
  const { i18n } = useTranslation();
  useDocumentTitle('Privacy Policy');
  return i18n.language.startsWith('es') ? <PrivacyContentEs /> : <PrivacyContentEn />;
}
```

```tsx
// Legal.tsx
import { useTranslation } from 'react-i18next';
import { useDocumentTitle } from '../../lib/use-document-title';
import LegalContentEn from './content/legal.en';
import LegalContentEs from './content/legal.es';

export function Legal() {
  const { i18n } = useTranslation();
  useDocumentTitle('Legal');
  return i18n.language.startsWith('es') ? <LegalContentEs /> : <LegalContentEn />;
}
```

(Confirm `useDocumentTitle`'s import path + signature against `SettingsPage.tsx` usage; adjust if it takes a key vs. a literal.)

- [ ] **Step 4: Run to verify it passes**

Run: `pnpm --dir ProjectCeres.Client exec vitest run src/app/pages/legal/legal-pages.test.tsx`
Expected: PASS (4/4).

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/pages/legal/Privacy.tsx ProjectCeres.Client/src/app/pages/legal/Legal.tsx ProjectCeres.Client/src/app/pages/legal/legal-pages.test.tsx
git commit -m "feat(13): /privacy + /legal page components with anchored sections"
```

---

### Task 8: Consent banner + App wiring

**Files:**
- Create: `ProjectCeres.Client/src/app/components/ConsentBanner.tsx`
- Test: `ProjectCeres.Client/src/app/components/ConsentBanner.test.tsx`
- Modify: `ProjectCeres.Client/src/app/App.tsx` (routes + mount banner)
- Modify: `ProjectCeres.Client/src/app/layout/AuthLayout.tsx` (use `<Footer>`)

**Interfaces:**
- Consumes: `readConsent`/`writeConsent`/`clearConsent` (Task 1), `onOpenConsentManager` (Task 5), i18n keys (Task 6), base-ui primitives (`sheet` or a custom bottom region, `switch`, `button`, `separator`).
- Produces: `export function ConsentBanner()` — on mount, shows itself iff `readConsent() === null`; subscribes to `onOpenConsentManager` to re-open even after a choice. First layer: **Accept all** + equally-prominent **Reject all** (same `<Button>` variant, ≥44×44 on mobile) + **Manage preferences** (expands granular toggles: necessary on+disabled; analytics + preferences real `<Switch>`es). Saving writes the cookie and hides the banner. No trailing ellipsis in labels.

- [ ] **Step 1: Write the failing banner test**

```tsx
import { describe, it, expect, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConsentBanner } from './ConsentBanner';
import { CONSENT_COOKIE_NAME, readConsent } from '../i18n/consent-cookie';
import { openConsentManager } from '../i18n/consent-events';

beforeEach(() => { document.cookie = `${CONSENT_COOKIE_NAME}=; Path=/; Max-Age=0`; });

describe('ConsentBanner', () => {
  it('shows when no consent cookie is present', () => {
    render(<ConsentBanner />);
    expect(screen.getByRole('button', { name: /accept all/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /reject all/i })).toBeInTheDocument();
  });

  it('accept-all and reject-all use the same button variant (equal prominence)', () => {
    render(<ConsentBanner />);
    const accept = screen.getByRole('button', { name: /accept all/i });
    const reject = screen.getByRole('button', { name: /reject all/i });
    expect(accept.className).toEqual(reject.className);
  });

  it('accept-all persists consent and hides the banner', async () => {
    render(<ConsentBanner />);
    await userEvent.click(screen.getByRole('button', { name: /accept all/i }));
    expect(readConsent()!.categories.analytics).toBe(true);
    expect(screen.queryByRole('button', { name: /accept all/i })).not.toBeInTheDocument();
  });

  it('reject-all persists necessary-only and hides the banner', async () => {
    render(<ConsentBanner />);
    await userEvent.click(screen.getByRole('button', { name: /reject all/i }));
    const rec = readConsent()!;
    expect(rec.categories.necessary).toBe(true);
    expect(rec.categories.analytics).toBe(false);
    expect(rec.categories.preferences).toBe(false);
  });

  it('does not show when a consent cookie already exists', () => {
    render(<ConsentBanner />);
    // dismiss first
    return userEvent.click(screen.getByRole('button', { name: /accept all/i })).then(() => {
      const { container } = render(<ConsentBanner />);
      expect(container.querySelector('button')).toBeNull();
    });
  });

  it('re-opens on openConsentManager even after a choice', async () => {
    render(<ConsentBanner />);
    await userEvent.click(screen.getByRole('button', { name: /accept all/i }));
    expect(screen.queryByRole('button', { name: /accept all/i })).not.toBeInTheDocument();
    openConsentManager();
    expect(await screen.findByRole('button', { name: /accept all/i })).toBeInTheDocument();
  });

  it('manage preferences reveals the analytics + preferences toggles', async () => {
    render(<ConsentBanner />);
    await userEvent.click(screen.getByRole('button', { name: /manage preferences/i }));
    expect(screen.getByRole('switch', { name: /analytics/i })).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: /preferences/i })).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `pnpm --dir ProjectCeres.Client exec vitest run src/app/components/ConsentBanner.test.tsx`
Expected: FAIL — `ConsentBanner` not found.

- [ ] **Step 3: Write the banner**

Implement per the Interfaces contract. Use `useState(() => readConsent() === null)` for initial visibility and a `useEffect` subscribing to `onOpenConsentManager` to set visible again. `Accept all` → `writeConsent({necessary:true, analytics:true, preferences:true})`; `Reject all` → `writeConsent({necessary:true, analytics:false, preferences:false})`; `Save preferences` (in manage mode) → write the toggle state. Necessary toggle rendered `checked disabled`. All copy via `t('legal.banner.*')`. base-ui `<Switch>` uses `onCheckedChange` — confirm against `src/components/ui/switch.tsx`. Fixed bottom region, `role="region"` + `aria-label`, ≥44px touch targets, does not obscure page content (bottom-anchored, page scrolls behind).

- [ ] **Step 4: Run to verify it passes**

Run: `pnpm --dir ProjectCeres.Client exec vitest run src/app/components/ConsentBanner.test.tsx`
Expected: PASS (7/7).

- [ ] **Step 5: Wire routes + mount banner in `App.tsx`**

Add to the public branch (relative paths), under a new `LegalLayout` route element (sibling of the `AuthLayout` route):

```tsx
<Route element={<LegalLayout />}>
  <Route path="privacy" element={<Privacy />} />
  <Route path="legal" element={<Legal />} />
</Route>
```

Mount `<ConsentBanner />` right after `<Toaster />` (root, outside `<Routes>`) so it shows on every surface. Add the imports.

- [ ] **Step 6: Swap AuthLayout's footer to `<Footer>`**

Replace `AuthLayout`'s bare `<footer>` (LanguageToggle + ThemeToggle) with `<Footer />` so the public auth pages carry the legal links (LSSI-CE). Keep `<ThemeToggle>` reachable — add it to `<Footer>` or keep it alongside; confirm the footer still shows the theme toggle on auth pages. (Decide in-step; the a11y test `AuthLayout.a11y.test.tsx` must still pass.)

- [ ] **Step 7: Run the affected suites**

Run:
```bash
pnpm --dir ProjectCeres.Client exec vitest run src/app/components/ConsentBanner.test.tsx src/app/layout/AuthLayout.test.tsx src/app/layout/AuthLayout.a11y.test.tsx
```
Expected: all PASS (fix AuthLayout tests if the footer swap changed queried text — update the tests to match the new true footer, never weaken them).

- [ ] **Step 8: Commit**

```bash
git add ProjectCeres.Client/src/app/components/ConsentBanner.tsx ProjectCeres.Client/src/app/components/ConsentBanner.test.tsx ProjectCeres.Client/src/app/App.tsx ProjectCeres.Client/src/app/layout/AuthLayout.tsx
git commit -m "feat(13): consent banner mounted at root; /privacy + /legal routes; Footer on AuthLayout"
```

---

### Task 9: Settings "Privacy & cookies" re-entry section

**Files:**
- Modify: `ProjectCeres.Client/src/app/features/settings/SettingsPage.tsx`
- Modify: `ProjectCeres.Client/src/app/features/settings/SettingsPage.test.tsx`

**Interfaces:**
- Consumes: `openConsentManager` (Task 5), i18n keys (Task 6), the existing `Card`/`Link`/`Button` grammar already in `SettingsPage`.
- Produces: a new `<Card>` section matching the page's existing Security/Account card shape — title "Privacy & cookies", a muted description, a "Manage cookie preferences" button (calls `openConsentManager`), and `<Link>`s to `/privacy` and `/legal`. **No footer on AppLayout.**

- [ ] **Step 1: Add the failing assertion to `SettingsPage.test.tsx`**

```tsx
it('shows a Privacy & cookies section with a manage-preferences action', async () => {
  // render SettingsPage with its existing test harness/mocks
  expect(await screen.findByRole('heading', { name: /privacy & cookies/i })).toBeInTheDocument();
  expect(screen.getByRole('button', { name: /manage cookie preferences/i })).toBeInTheDocument();
  expect(screen.getByRole('link', { name: /privacy policy/i })).toHaveAttribute('href', '/privacy');
});
```

(Match the file's existing render/mocks for `useApi`; copy the harness from a sibling test in the same file.)

- [ ] **Step 2: Run to verify it fails**

Run: `pnpm --dir ProjectCeres.Client exec vitest run src/app/features/settings/SettingsPage.test.tsx`
Expected: FAIL on the new assertion.

- [ ] **Step 3: Add the section to `SettingsPage`**

Insert, after the Account `<Card>`, a `<Card>` following the exact same grammar (CardHeader/CardTitle + CardContent with muted `<p>` + actions). A `<Button variant="outline" onClick={openConsentManager}>` for manage-cookies, and `<Link>`s to `/privacy` + `/legal` styled via `buttonVariants`. Copy via `t('legal.settings.*')`.

- [ ] **Step 4: Run to verify it passes**

Run: `pnpm --dir ProjectCeres.Client exec vitest run src/app/features/settings/SettingsPage.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ProjectCeres.Client/src/app/features/settings/SettingsPage.tsx ProjectCeres.Client/src/app/features/settings/SettingsPage.test.tsx
git commit -m "feat(13): Settings Privacy & cookies re-entry section (no AppLayout footer)"
```

---

### Task 10: E2E golden path + full-suite gate + roadmap close

**Files:**
- Create: `ProjectCeres.Client/e2e/consent-legal.spec.ts` (confirm the e2e dir name/location in Step 1 — search for the existing Playwright config and spec convention)
- Modify: `docs/roadmap-phase-three.md` (tick the Stage 13 items this slice covers)

**Interfaces:**
- Consumes: the full wired app.
- Produces: an E2E spec covering the golden path; a green full client suite; ticked roadmap items.

- [ ] **Step 1: Locate the Playwright convention**

Run:
```bash
find ProjectCeres.Client -name "playwright.config.*" -o -ipath "*e2e*" -name "*.spec.ts" | head
```
Read one existing spec + the config to match the base URL, dev-server setup, and selectors convention. Place the new spec per that convention.

- [ ] **Step 2: Write the E2E spec**

Cover:
- First visit (clear cookies) → banner shows → click Accept all → reload → banner gone.
- Signed-out navigation to `/privacy` renders the policy (public reachability).
- `/legal#cookies` scrolls to / resolves the cookies section.
- Settings (authed) → "Manage cookie preferences" re-opens the banner. (Use the existing auth helper if the e2e suite has one; if authed E2E is heavy, cover the Settings re-open with the Vitest test from Task 8 and note it here.)
- 375px viewport: banner buttons ≥44×44, no horizontal overflow.

- [ ] **Step 3: Run the E2E spec**

Run the project's E2E command (from the config, e.g. `pnpm --dir ProjectCeres.Client exec playwright test consent-legal`).
Expected: PASS. If the environment can't run Playwright headlessly, say so explicitly and hand the UX checklist to the user with the specific URLs (`/privacy`, `/legal`, Settings), per `feedback_e2e_before_asking_manual_approval`.

- [ ] **Step 4: Full client suite + build gate**

Run:
```bash
pnpm --dir ProjectCeres.Client test
pnpm --dir ProjectCeres.Client build
```
Expected: both exit 0. Root-cause any failure now (never skip/weaken).

- [ ] **Step 5: Tick the roadmap Stage 13 items**

In `docs/roadmap-phase-three.md`, mark the privacy-page + banner + consent-revocation items `[x]` for items now covered by automated tests; leave manual-only items `[ ]` and flag them. Do NOT close Stage 13 (items D + 13.b remain) — this is a slice, not the whole stage.

- [ ] **Step 6: Commit**

```bash
git add ProjectCeres.Client/e2e/consent-legal.spec.ts docs/roadmap-phase-three.md
git commit -m "test(13): E2E consent/legal golden path; tick covered Stage 13 items"
```

---

## Self-Review

**1. Spec coverage** — each spec in-scope row maps to a task:
- `/privacy` + `/legal` routes + pages → Tasks 7, 8 (routes)
- New public layout → Task 4
- Cookie consent banner (granular, equal-prominence) → Task 8
- `consent-cookie.ts` → Task 1
- Shared `<Footer>` on public layouts → Tasks 5, 8
- Authed consent re-entry in Settings → Task 9
- Prose design token/recipe → Task 2
- Placeholder legal text (per-language files) → Task 3
- EN/ES short copy → Task 6
- Mobile/a11y → Tasks 8, 10
- Testing (Vitest + E2E) → every task's tests + Task 10
- Deferrals (`/accesibilidad`, consent-ledger) → already homed in the roadmap (commit `f0c17f70`); nothing to build.

**2. Placeholder scan** — the only intentional unfilled content is the legal draft's `[CONFIRM WITH COUNSEL: …]` markers (a hard requirement — unverifiable business/legal facts must stay unfilled, never guessed). No TBD/TODO steps; every code step has real code.

**3. Type consistency** — `ConsentCategories`/`ConsentRecord` defined in Task 1 and consumed with the same shape in Task 8; `openConsentManager`/`onOpenConsentManager` defined in Task 5 and consumed in Tasks 8, 9; content-component default exports named in Task 3 and imported in Task 7.

**Open items the executor resolves against the live code (shapes fixed, details confirmed on read):**
- `index.css` recipe idiom (`@layer components` vs `@utility`) — Task 2 Step 1.
- `useDocumentTitle` signature — Task 7 Step 3.
- base-ui `<Switch>` prop name — Task 8 Step 3.
- Playwright dir/config + auth helper — Task 10 Step 1.
- Whether an i18n-parity test exists — Task 6 Step 3.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-10-04-stage-13-cookie-consent-legal-pages.md`. Two execution options:

1. **Subagent-Driven (recommended)** — a fresh subagent per task, review between tasks, fast iteration. Each UI task routes through `frontend-orchestrator`.
2. **Inline Execution** — execute tasks in this session using `executing-plans`, batch execution with checkpoints.

Which approach?
