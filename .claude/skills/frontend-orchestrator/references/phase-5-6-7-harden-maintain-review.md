# Phases 5, 6 & 7 — Hardening, system maintenance, pure review

## Phase 5 — Hardening before merge

The user said "ready to ship", "polish before merge", "production-ready", "what's missing", or you're about to commit a Phase 1/2/3 result.

1. **`/impeccable harden`** — edge cases, i18n, empty states, error states, overflow, long-text, missing-data. (`/onboard` merged into `/harden` in impeccable v2.1; first-run experiences and empty states are now part of harden's scope.)
2. **`/impeccable optimize`** — LCP, bundle size, runtime perf. Distinct from `vercel-react-best-practices` — that skill enforces React patterns; this one diagnoses actual measurements.
3. **`/impeccable polish`** — final 5-dimension design-system alignment pass with P0–P3 severity. Catches drift between what was built and what `docs/design-system.md` documents.
4. **`web-design-guidelines`** — final audit. CLAUDE.md already requires this.
5. **UX/UI verification checklist** — same as Phase 1 step 7.

All four sub-steps are required. Claiming a Phase 5 pass while skipping any of them is a quality regression.

## Phase 6 — System maintenance (after shipping a pattern used in 2+ places)

The user said "this pattern is now in three places, extract it", or you notice a recipe being copy-pasted across components.

1. **`/impeccable extract`** — identifies the reusable component, token, or pattern. Tell it: *"Write the extracted primitive into `ProjectCeres.Client/src/components/ui/` (shadcn convention). Add any new token to `ProjectCeres.Client/src/index.css`. Do not write a `DESIGN.md` — the project's design system lives at `docs/design-system.md`."*
2. **`sync-docs` skill** — update `docs/design-system.md` to document the new primitive, its variants, and when to use it. Same commit as the extraction.

## Phase 7 — Pure review (no implementation request)

The user said "review this UI", "audit this page", "what's wrong with this", "check this against the design system".

- **For a single dimension** (typography, color, motion, layout): `/impeccable polish` — 5-dimension scoring, design-system alignment.
- **For overall usability and quality**: `/impeccable critique` — Nielsen heuristics, persona sub-agents, detector.
- **For accessibility specifically**: `web-design-guidelines` first, then `/impeccable critique` for the rest.
- **For perf specifically**: `vercel-react-best-practices` first (pattern-level prevention — re-render hygiene, bundle/import). Escalate to `/impeccable optimize` only if measurements are needed (LCP, INP, CLS). The two skills are complements, not substitutes: prevention catches patterns at write-time; optimize diagnoses the production app's actual bottlenecks.
- **For high-stakes surfaces** (sign-up flow, billing, account deletion): run all three — `polish`, `critique`, and `web-design-guidelines`. Cost is worth it.
