# ADR 0014: React as the Frontend Framework (Phase 2+)

## Status: Accepted

## Context
Phase 1 uses ASP.NET Core MVC with Razor Views for server-side rendering. No JavaScript
framework is needed to start — forms submit to the server, the server responds with a
rendered page.

As the project progresses toward a hosted, multi-user product (Phase 3) with a business
model (Phase 5), the frontend requirements grow: interactive charts, a tool-like UI behind
authentication, and a likely mobile app. A frontend framework decision was needed before
Phase 2 begins to avoid choosing under time pressure or mid-phase.

Options considered:
- **Razor Views only (forever):** Simple, no JS complexity, but limits interactivity and
  makes a future mobile app impossible to share code with.
- **Vue / Svelte / Angular:** Smaller ecosystems, fewer hiring options, no direct path to
  mobile.
- **React:** Dominant market share, large ecosystem, React Native for mobile, Next.js for
  SSR on any public-facing pages. Aligns with the developer's employability goals.

## Decision
React is the chosen framework when the transition away from Razor Views occurs. The
migration happens in three stages:

| Stage | Phase | Description |
|-------|-------|-------------|
| Razor only | Phase 1 (current) | No React. No JS framework. |
| Hybrid | Phase 2 | Embed React components into specific Razor pages for interactive UI (e.g. dashboard charts). MVC stays intact — no API needed yet. |
| Full SPA evaluation | Phase 3+ | Evaluate full SPA. App is hosted, behind auth, no SEO concern for authenticated pages — the SPA model fits. |

When the full SPA transition occurs, the ASP.NET Core MVC backend must be decoupled into
a pure Web API. This is the natural point because:
- A standalone API is reusable by a future React Native mobile app
- The app is behind authentication, so SEO is not a concern for the main app
- It cleanly separates frontend and backend responsibilities

**SEO note:** Any public-facing pages outside the login wall (landing page, pricing,
marketing) must use server-side rendering — Next.js or a separate static site — to remain
indexable. Do not assume the entire app is exempt from SEO. Flag this when building any
public-facing pages in Phase 3+.

## Consequences

**Positive:**
- Single framework from Phase 2 through the full SPA transition — no mid-project switch
- React Native provides a credible mobile path without rewriting business logic
- Large ecosystem and community; easier to find help and libraries
- Aligns with the developer's employability goals

**Negative:**
- Introduces a JS build toolchain (Vite or similar) in Phase 2 — new complexity for a
  developer currently working in a pure .NET stack
- Full SPA transition (Phase 3+) requires decoupling MVC into a Web API — a non-trivial
  architectural change (see Open Questions in planning.md)
- React Native mobile app is a separate project with its own scope and cost — the
  decision to build it is not made here, only that React keeps the option open
