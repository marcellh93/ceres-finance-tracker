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
| `/privacy` route + page | Privacy Policy, EN+ES, public, long-form prose. Placeholder text structured on the Art. 13/14 headings (`security-model.md`) + the retention-window disclosure (`legal.md`). |
| `/legal` route + page | Aviso Legal + Cookie Policy as anchored sections (`#aviso-legal`, `#cookies`) — satisfies "independently addressable". Public, EN+ES. |
| New public page layout | Minimal: brand header + scrollable prose + footer. (`AuthLayout` = 420px card, `AppLayout` = authed shell — neither fits long-form public text.) |
| Cookie consent banner | Full AEPD granular architecture (future-ready): first layer = **Accept all** + equally-prominent **Reject all** (two equal-weight buttons, ≥44×44px mobile) + **Manage preferences** → granular toggles (necessary on/locked; analytics + preferences real switches that persist a choice but gate nothing today). Mounts above both layouts (like root `<Toaster>`) so it shows pre-auth + authed. First-visit = absence of consent cookie. No trailing ellipsis in labels. |
| `consent-cookie.ts` | Client utility (sibling of `lang-cookie.ts`): first-party, `SameSite=Lax; Secure; Path=/`, not HttpOnly, stores category choices + timestamp, **24-month** Max-Age (AEPD re-consent), no identifier. |
| Shared `<Footer>` | Legal links (Aviso Legal / Privacy / Cookie) + "Manage cookie preferences" (re-opens banner) + language toggle. Mounted on **public layouts only** (AuthLayout + new legal-page layout) — LSSI-CE Art. 10 requires the footer on public pages. |
| Authed consent re-entry | A "Privacy & cookies" section in **Settings** (≤2 clicks): legal links + "Manage cookie preferences" action re-opening the banner. **No footer on `AppLayout`** — SaaS norm + the authed shell's `h-screen` grid shouldn't gain a footer row. Satisfies GDPR Art. 7(3) "withdraw as easy as give". |
| Prose design token/recipe | New long-form container recipe in `index.css` + documented in `design-system.md`. |
| Placeholder legal text | Separate per-language content files (e.g. `privacy.en.md`/`privacy.es.md`) imported by the pages — counsel edits readable documents, not JSON. **Clearly marked PLACEHOLDER**; real counsel text drops in later. EN/ES parity. |
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
- **Real legal text** — placeholder only; counsel supplies the binding wording.

## Binding requirements (from the docs — build obligations)

- **AEPD 2024** (`security-model.md` § Cookie Consent): Accept-all + equally-prominent
  Reject-all in the first layer (reject not smaller/less-colorful/extra-click);
  granular per-purpose categories; withdrawal as easy as giving; 24-month consent
  validity; publish the banner + Política de Cookies even though only necessary
  cookies are set.
- **Art. 13/14** (`security-model.md`): the privacy policy placeholder is structured
  on the mandated fields (data types, legal basis, retention, sharing/subprocessors,
  user rights) so counsel text drops into the right headings.
- **LSSI-CE Art. 10**: Aviso Legal (legal name, NIF, address, contact) in the footer
  on public pages.
- **Retention disclosure** (`legal.md`): the 30-day grace / 180-day archive windows
  must be stated in the policy placeholder.

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
