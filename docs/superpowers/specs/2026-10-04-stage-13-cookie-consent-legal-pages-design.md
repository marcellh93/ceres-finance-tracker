# Stage 13 — Cookie Consent Banner + Legal Pages — Design

**Date:** 2026-10-04
**Phase:** 3 (Hosted Beta)
**Roadmap:** `docs/roadmap-phase-three.md` § Stage 13 (GDPR baseline) — Privacy policy + cookie consent
**Status:** Design approved; pending implementation plan.

## Goal

Ship the EU-beta legal-gate UI: a `/privacy` and `/legal` page (EN+ES), a cookie
consent banner, and a reachable consent-revocation affordance — the two remaining
unbuilt Stage 13 items (13.1 privacy text+page, 13.2 banner). Built for a
non-expert audience; minimal friction. **Client-only — no backend, no entity, no
migration** (verified: no server-side consent code exists; the IUserOwned /
registry / verify-stage-completeness gates do NOT apply).

## Context established by pre-design research

- **No live trackers to gate.** `planning-resolved.md` records "No third-party
  behavioural analytics in Phase 3"; the app sets only strictly-necessary cookies
  (session, CSRF, `lang`), all AEPD-2024 consent-exempt. The Stage 14 CSP
  (`default-src 'self'; script-src 'self' …`) already blocks third-party scripts at
  the browser, so the banner is **not** the pre-consent enforcement layer — it is a
  disclosure surface with a granular architecture that currently gates nothing.
- **Public-route pattern exists** — `App.tsx` has a public `<Route element={<AuthLayout />}>`
  branch (login/register/erasure-cancel); the SPA shell is served `AllowAnonymous()`.
  `/privacy` + `/legal` slot as public routes — no server route work.
- **Consent-cookie pattern exists** — `src/app/i18n/lang-cookie.ts` (`SameSite=Lax;
  Secure; Path=/; Max-Age`, not HttpOnly, no PII) is the exact template for
  `consent-cookie.ts`.
- **Primitives exist** — `sheet`, `switch`, `button`, `separator`, `alert-dialog`.
- **Design-system gap** — no long-form prose container/recipe exists (only font
  guidance). One new token/recipe required (added to `index.css` + documented in
  `design-system.md` before use, per design-system-first).
- **i18n ready** — `react-i18next`, cookie-driven language, `en.json`/`es.json` at
  strict line parity (342/342). Short copy → JSON (both files); long prose → separate
  per-language content files.

## Scope

### In scope

| Item | Detail |
|---|---|
| `/privacy` route + page | Privacy Policy, EN+ES, public, long-form prose. Codebase-grounded draft (see below) structured on the Art. 13/14 headings (`security-model.md`) + the retention-window disclosure (`legal.md`). |
| `/legal` route + page | Aviso Legal + Cookie Policy as anchored sections (`#aviso-legal`, `#cookies`) — satisfies "independently addressable". Public, EN+ES. |
| New public page layout | Minimal: brand header + scrollable prose + footer. (`AuthLayout` = 420px card, `AppLayout` = authed shell — neither fits long-form public text.) |
| Cookie consent banner | Full AEPD granular architecture (future-ready): first layer = **Accept all** + equally-prominent **Reject all** (two equal-weight buttons, ≥44×44px mobile) + **Manage preferences** → granular toggles (necessary on/locked; analytics + preferences real switches that persist a choice but gate nothing today). Mounts above both layouts (like root `<Toaster>`) so it shows pre-auth + authed. First-visit = absence of consent cookie. No trailing ellipsis in labels. |
| `consent-cookie.ts` | Client utility (sibling of `lang-cookie.ts`): first-party, `SameSite=Lax; Secure; Path=/`, not HttpOnly, stores category choices + timestamp, **24-month** Max-Age (AEPD re-consent), no identifier. |
| Shared `<Footer>` | Legal links (Aviso Legal / Privacy / Cookie) + "Manage cookie preferences" (re-opens banner) + language toggle. Mounted on **public layouts only** (AuthLayout + new legal-page layout) — LSSI-CE Art. 10 requires the footer on public pages. |
| Authed consent re-entry | A "Privacy & cookies" section in **Settings** (≤2 clicks): legal links + "Manage cookie preferences" action re-opening the banner. **No footer on `AppLayout`** — SaaS norm + the authed shell's `h-screen` grid shouldn't gain a footer row. Satisfies GDPR Art. 7(3) "withdraw as easy as give". |
| Prose design token/recipe | New long-form container recipe in `index.css` + documented in `design-system.md`. |
| Codebase-grounded legal draft | Per-language content files (TSX) carrying a **real first draft grounded in verified codebase + `legal.md` facts** — NOT bare placeholders. Every claim I can verify from the code is stated truthfully; every business-fact or legal-judgment I cannot verify is a clearly-marked `[CONFIRM WITH COUNSEL: …]` inline blank. **Not legally reviewed; not live until a human (ideally counsel) reviews it** — a banner at the top of each document says so. EN/ES parity. See "Grounded-draft content rules" below. |
| EN/ES short copy | Banner, footer, Settings-section labels → new i18n keys in both `en.json` + `es.json`. |
| Mobile/a11y | ≥44×44 touch targets; no overflow ≥320px; 375px readability; banner doesn't obscure critical content; verified at the UX checklist. |

### Out of scope

- **`/accesibilidad`** (EAA accessibility statement) — a legal Tier-1 item but NOT in
  the Stage 13 checklist. Deferred + logged as its own roadmap item.
- **Server-side consent ledger** (GDPR Art. 7(1) demonstrability) — not required for
  strictly-necessary-only cookies; defer with the "upgrade when a tracker is added"
  path (same note as `planning-resolved.md`'s upgradeable-banner clause).
- **Real tracker-gating** — nothing to gate (no analytics in Phase 3). The toggles
  persist a preference that currently gates nothing.
- **Terms of Service page** — not in this slice.
- **Final/binding legal text** — the draft is a grounded first draft with `[CONFIRM WITH COUNSEL]` markers, NOT counsel-approved wording. It must not go live as the published policy until a human reviews it. I do not author the business-identity facts (legal name, NIF, address), the final subprocessor list, or the binding per-purpose legal-basis wording — those are counsel's.

## Binding requirements (from the docs — build obligations)

- **AEPD 2024** (`security-model.md` § Cookie Consent): Accept-all + equally-prominent
  Reject-all in the first layer (reject not smaller/less-colorful/extra-click);
  granular per-purpose categories; withdrawal as easy as giving; 24-month consent
  validity; publish the banner + Política de Cookies even though only necessary
  cookies are set.
- **Art. 13/14** (`security-model.md`): the privacy draft is structured on the
  mandated fields (data types, legal basis, retention, sharing/subprocessors, user
  rights). The verifiable fields carry real grounded content; the legal-basis
  wording and subprocessor list carry `[CONFIRM WITH COUNSEL]` markers.
- **LSSI-CE Art. 10**: Aviso Legal (legal name, NIF, address, contact) in the footer
  on public pages. These are business-identity facts — all `[CONFIRM WITH COUNSEL]`.
- **Retention disclosure** (`legal.md`): the 30-day grace / 150-day archive /
  180-day deletion windows AND the 6-year financial-record retention that overrides
  erasure (Art. 17(3)(b)) must be stated in the policy — as verified fact, not blanks.

## Grounded-draft content rules

The draft states **verified facts truthfully** and marks **everything unverifiable**
as `[CONFIRM WITH COUNSEL: …]`. Each document opens with a banner:
`[DRAFT — generated from the codebase, NOT legally reviewed. Do not publish as the
live policy until counsel has reviewed it.]`

**Facts verified from the codebase / `legal.md` (state these as fact in the draft):**

- **Cookies set** (all strictly-necessary, no tracking; `__Host-`-prefixed where
  server-set): `__Host-Session` (session), `__Host-Persist` (remember-me),
  `__Host-XSRF` (CSRF), `lang` (language preference), `cookie_consent` (this
  banner's own choice record). Source: `SessionConstants.cs`,
  `LanguagePreferenceMiddleware.cs`, `consent-cookie.ts`. The Cookie Policy lists
  exactly these; none require consent today.
- **Personal data categories collected:** account email; IP address + user-agent
  (security logs — `FailedLoginAttempt`, `UserSession`, `AuditLog`, `UserBlockedIp`);
  uploaded attachment file names + the financial attachments themselves; the
  user's financial data (accounts, transactions, budgets, categories). Source:
  `ProjectCeres/Models/*`.
- **Retention windows** (from `legal.md` § Data Retention Policy — state exactly):
  soft-deleted saved reports 90 days; audit logs 12 months; failed-login logs
  1 year; natural-churn account closure 30-day grace → 150-day sealed archive →
  permanent deletion at day 180; GDPR-erasure anonymisation immediate with a
  72-hour cancel-only hold.
- **The erasure limit that a template would get WRONG:** financial/accounting
  records are retained **6 years** (Código de Comercio Art. 30) / tax-relevant
  4–6 years (Ley General Tributaria), and this **overrides the right to erasure**
  for those records (GDPR Art. 17(3)(b)). On erasure, personal identifiers are
  anonymised but anonymised financial records are retained for the legal period.
  State this plainly — it is the single most important correctness point.
- **Rights-exercise flow (real endpoints/surfaces):** data export (portability) via
  the Account settings "download a copy" action (`POST /api/profile/export` →
  `GET /api/profile/export/download`); erasure via `POST /api/profile/erasure`
  with a 72-hour emailed cancel link (`/erasure/cancel`). The draft describes the
  user-facing path (Settings → Account), not the endpoint names.

**Marked `[CONFIRM WITH COUNSEL: …]` (never invented):**

- Business identity for the Aviso Legal: legal entity name, NIF, registered
  address, contact email (LSSI-CE Art. 10 mandatory facts — unknown from code).
- Final subprocessor list: `legal.md` names "hosting provider, email service" as
  TBD with DPAs required; the draft lists these as `[CONFIRM: hosting provider —
  name + DPA]`, `[CONFIRM: email service — name + DPA]`, not guessed vendors.
- Per-purpose **legal basis** wording (Art. 6): the draft proposes the likely basis
  per purpose (contract for the service; legal obligation for financial retention;
  legitimate interest for security logs) each wrapped as
  `[CONFIRM WITH COUNSEL: legal basis — <proposed>]` — a proposal for counsel to
  ratify, not an assertion.
- DPO / supervisory-authority contact details; the effective date.

**Hard line:** no `[CONFIRM …]` blank is ever silently filled with a plausible
guess. An unfilled blank is correct and honest; a guessed legal fact is a liability.
`docs/legal.md` stays read-only — this spec reads facts from it, never edits it.

## Conventions

- **Frontend-orchestrator owns the build** (CLAUDE.md § Frontend Work) — the
  implementation routes through it; no ad-hoc `frontend-design`/`impeccable`.
- **Design-system-first** — the prose token/recipe lands in `index.css` +
  `design-system.md` before any page consumes it; no hex/ad-hoc spacing.
- **Show-then-approval + UX checklist** — show rendered result, wait for approval;
  run golden/empty/error/375px/all-nav checklist; prefer Playwright E2E as
  verification over a manual handoff (`feedback_e2e_before_asking_manual_approval`).
- **EN/ES parity is a ship gate** — every new short-copy key in both JSON files;
  both policy content files present.
- **base-ui primitives** (not Radix) — reuse the existing `src/components/ui/`
  shapes; no Radix idioms.

## Testing (tests-as-ship-gate)

- **Vitest/RTL:** banner shows when no consent cookie; hides after a choice; Accept-all
  and Reject-all both persist the cookie; "Manage preferences" opens the granular
  toggles; revoke action clears the cookie and re-shows the banner. Equal-prominence
  (both buttons same variant) asserted. `/privacy` + `/legal` render; `/legal`
  anchors resolve.
- **E2E (Playwright):** first-visit banner → accept → reload shows no banner →
  Settings "Manage cookie preferences" re-opens it; public `/privacy` reachable
  unauthenticated. Covers the golden path so a manual handoff isn't the verification.
- **375px:** banner buttons ≥44×44, no overflow; legal pages readable.

## Definition of Done

- `/privacy` + `/legal` public routes render EN+ES from the per-language content files;
  `/legal` anchored sections addressable.
- Banner (granular, equal-prominence, ≥44×44) mounts pre-auth + authed; first-visit
  logic + `consent-cookie.ts` (24-month) working.
- Footer on public layouts; Settings "Privacy & cookies" section with revoke in the
  authed app (no AppLayout footer).
- Prose recipe in `index.css` + `design-system.md`.
- EN/ES parity held (both JSON + both content files); Vitest + E2E green; `pnpm build`
  within budget; 375px verified.
- Roadmap Stage 13 items ticked; `/accesibilidad` + consent-ledger deferrals logged.

## Open implementation details (the plan decides; shapes are fixed)

- Exact prose recipe (max-width, line-height, heading rhythm) — a design pass in the
  frontend-orchestrator build.
- Banner as `sheet` (bottom) vs a custom bottom-region — pick the one that meets
  equal-prominence + non-obstruction cleanly.
- Content-file format (Markdown rendered vs. structured TSX) — whichever keeps counsel
  editing simple and renders safely under the CSP (no `dangerouslySetInnerHTML`
  unless it goes through the documented exception).
